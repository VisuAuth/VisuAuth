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
            return await IssueOrUnauthorized(issuer, userId, cancellationToken);
        }

        return SignInApiResponseMapper.MapFailure(result);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IAuthenticationFlow authentication,
        ITenantContext tenantContext,
        IJwtIssuer issuer,
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

        return await IssueOrUnauthorized(issuer, registerResult.ResourceId!, cancellationToken);
    }

    private static async Task<IResult> RefreshAsync(
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

        return TokenOrUnauthorized(token);
    }

    private static async Task<IResult> IssueOrUnauthorized(
        IJwtIssuer issuer,
        string userId,
        CancellationToken cancellationToken)
        => TokenOrUnauthorized(await issuer.IssueAsync(userId, cancellationToken));

    /// <summary>
    /// Maps an issued token to 200, or a null (user gone, locked out, or — on
    /// the refresh path — a revoked security stamp) to a generic 401. The
    /// message is deliberately uniform so it does not disclose which of those
    /// applies.
    /// </summary>
    private static IResult TokenOrUnauthorized(JwtTokenResult? token)
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
            token.TenantId));
    }
}
