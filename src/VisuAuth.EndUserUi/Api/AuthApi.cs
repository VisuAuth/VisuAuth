using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.EndUserUi.Authentication;

namespace VisuAuth.EndUserUi.Api;

/// <summary>
/// Minimal-API endpoints under <c>/visuauth/api/auth</c>. The native /
/// mobile channel: clients post credentials and receive a signed JWT they
/// attach as <c>Authorization: Bearer ...</c> on subsequent requests.
/// </summary>
public static class AuthApi
{
    public const string SecurityStampClaimType = VisuAuthClaimTypes.SecurityStamp;

    /// <summary>
    /// Maps the three auth endpoints. Call from the consumer's
    /// <c>MapVisuAuthEndUserUi</c> chain (already wired via the meta-package).
    /// </summary>
    public static IEndpointRouteBuilder MapVisuAuthAuthApi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/visuauth/api/auth")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(prefix).WithTags("VisuAuth Auth");

        group.MapPost("/login", LoginAsync);
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/refresh", RefreshAsync);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAuthenticationFlow authentication,
        IJwtIssuer issuer,
        IRefreshTokenService refreshTokens,
        SignInAuditEmitter auditEmitter,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new AuthErrorResponse("Email and password are required."));
        }

        var attemptedEmail = request.Email.Trim();
        var result = await authentication.SignInWithPasswordAsync(
            attemptedEmail,
            request.Password,
            persistent: false,
            cancellationToken);

        // Audit emission first — even the failure path is recorded so the
        // admin log captures lockouts / failed attempts / 2FA hops.
        await auditEmitter.EmitAsync(result, attemptedEmail, SignInChannel.Api, cancellationToken: cancellationToken);

        // Success: emit a SECOND, more specific JWT-issued event before
        // we hand off to the issuer. The first (LoginSucceeded) is the
        // shared audit code; this one carries channel-specific intent so
        // operators can spot "API logins minted JWTs" patterns.
        if (result.Outcome == SignInOutcome.Success && result.UserId is { Length: > 0 } userId)
        {
            return await IssueOrUnauthorized(issuer, refreshTokens, userId, cancellationToken);
        }

        return SignInApiResponseMapper.MapFailure(result);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IAuthenticationFlow authentication,
        ITenantContext tenantContext,
        IJwtIssuer issuer,
        IRefreshTokenService refreshTokens,
        IAuditWriter audit,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new AuthErrorResponse("Email and password are required."));
        }

        if (!authentication.Capabilities.SupportsRegistration)
        {
            return Results.Json(
                new AuthErrorResponse("This backend does not support self-service registration."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var tenantId = tenantContext.IsMultiTenancyEnabled ? tenantContext.CurrentTenantId : null;

        var attemptedEmail = request.Email.Trim();
        var registerResult = await authentication.RegisterAsync(
            attemptedEmail,
            request.Password,
            tenantId,
            cancellationToken);

        if (!registerResult.IsSuccess)
        {
            await audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.UserRegistered,
                TargetType = AuditTargetTypes.User,
                TargetLabel = attemptedEmail,
                Outcome = AuditOutcome.Failure,
                FailureReason = registerResult.Error ?? string.Join("; ", registerResult.ValidationErrors),
                Payload = new Dictionary<string, string?> { [SignInAuditEmitter.ChannelPayloadKey] = "api" },
            }, cancellationToken);
            return Results.Json(
                new AuthErrorResponse(
                    registerResult.Error ?? "Failed to register.",
                    registerResult.ValidationErrors.Count > 0 ? registerResult.ValidationErrors : null),
                statusCode: StatusCodes.Status400BadRequest);
        }

        await audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.UserRegistered,
            TargetType = AuditTargetTypes.User,
            TargetId = registerResult.ResourceId,
            TargetLabel = attemptedEmail,
            Outcome = AuditOutcome.Success,
            Payload = new Dictionary<string, string?> { [SignInAuditEmitter.ChannelPayloadKey] = "api" },
        }, cancellationToken);

        return await IssueOrUnauthorized(issuer, refreshTokens, registerResult.ResourceId!, cancellationToken);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest? request,
        HttpContext httpContext,
        IJwtIssuer issuer,
        IJwtValidator validator,
        IRefreshTokenService refreshTokens,
        CancellationToken cancellationToken)
    {
        // With the refresh-token plugin on, an opaque single-use token is the
        // only accepted credential here. The access-token path is deliberately
        // closed rather than kept as a fallback: leaving it open would let an
        // attacker holding a leaked access token keep renewing it, which is the
        // very thing refresh tokens exist to stop.
        if (refreshTokens.IsEnabled)
        {
            return await RedeemRefreshTokenAsync(request, issuer, refreshTokens, cancellationToken);
        }

        return await ReissueFromAccessTokenAsync(httpContext, issuer, validator, cancellationToken);
    }

    /// <summary>
    /// Plugin path: redeem an opaque refresh token, rotating it. The rotated
    /// token comes back alongside a fresh access token; the presented one is
    /// now dead.
    /// </summary>
    private static async Task<IResult> RedeemRefreshTokenAsync(
        RefreshRequest? request,
        IJwtIssuer issuer,
        IRefreshTokenService refreshTokens,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Json(
                new AuthErrorResponse("A refreshToken is required."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var redemption = await refreshTokens.RedeemAsync(request.RefreshToken, cancellationToken);
        if (!redemption.Succeeded)
        {
            // Unknown, expired, revoked, or replayed — all reported the same so
            // a caller cannot probe for valid tokens.
            return Results.Json(
                new AuthErrorResponse("Refresh token is not valid."),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // The user may have been locked out or deleted since the refresh token
        // was minted, so the access token is still issued through the normal
        // eligibility checks.
        var token = await issuer.IssueAsync(redemption.UserId!, cancellationToken);
        return TokenOrUnauthorized(token, redemption.RotatedToken);
    }

    /// <summary>
    /// Legacy path, used when the refresh-token plugin is off: reissue from the
    /// presented (possibly expired) access token.
    /// </summary>
    private static async Task<IResult> ReissueFromAccessTokenAsync(
        HttpContext httpContext,
        IJwtIssuer issuer,
        IJwtValidator validator,
        CancellationToken cancellationToken)
    {
        // Refresh accepts the bearer header even if the token expired, but the
        // token must still be authentic: the validator verifies signature,
        // issuer, and audience (lifetime is deliberately skipped). Without
        // this a caller could forge any `sub` and have a fresh, fully valid
        // token minted for an arbitrary user — a pre-auth account takeover.
        if (!AuthenticationHeaderValue.TryParse(httpContext.Request.Headers.Authorization, out var auth) ||
            !string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(auth.Parameter))
        {
            return Results.Json(
                new AuthErrorResponse("Missing or malformed Authorization header."),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var validated = validator.ValidateSignatureAndRead(auth.Parameter);
        if (validated is null)
        {
            return Results.Json(
                new AuthErrorResponse("Bearer token failed validation."),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // ReissueAsync reloads the user, re-checks lockout, and — crucially —
        // requires the presented token's security stamp to still match the
        // user's current one. Without that check a token revoked via "revoke
        // sessions" could be exchanged here for a fresh, valid one: blocked at
        // protected endpoints, but laundered back to life through refresh.
        var token = await issuer.ReissueAsync(
            validated.Subject,
            validated.SecurityStamp,
            cancellationToken);

        return TokenOrUnauthorized(token, refreshToken: null);
    }

    /// <summary>
    /// Issues an access token for a freshly-authenticated user, plus a refresh
    /// token when the plugin is on. The refresh token is minted against the
    /// tenant baked into the access token, so it stays bound to the tenant the
    /// user actually belongs to.
    /// </summary>
    private static async Task<IResult> IssueOrUnauthorized(
        IJwtIssuer issuer,
        IRefreshTokenService refreshTokens,
        string userId,
        CancellationToken cancellationToken)
    {
        var token = await issuer.IssueAsync(userId, cancellationToken);
        if (token is null)
        {
            return TokenOrUnauthorized(token, refreshToken: null);
        }

        var refreshToken = await refreshTokens.IssueAsync(token.UserId, token.TenantId, cancellationToken);
        return TokenOrUnauthorized(token, refreshToken);
    }

    /// <summary>
    /// Maps an issued token to 200, or a null (user gone, locked out, or — on
    /// the refresh path — a revoked security stamp) to a generic 401. The
    /// message is deliberately uniform so it does not disclose which of those
    /// applies.
    /// </summary>
    private static IResult TokenOrUnauthorized(JwtTokenResult? token, string? refreshToken)
    {
        if (token is null)
        {
            return Results.Json(
                new AuthErrorResponse("User is no longer eligible to sign in."),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(new AuthSuccessResponse(
            token.AccessToken,
            token.ExpiresAt,
            token.UserId,
            token.Email,
            token.TenantId,
            refreshToken));
    }
}
