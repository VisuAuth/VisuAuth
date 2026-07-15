using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Resolves the current tenant from the request and stashes it on
/// <see cref="HttpContext.Items"/> for downstream consumers (the
/// <see cref="HttpContextTenantContext"/>, query filters, the save-changes
/// interceptor, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Resolution order, when enabled in <see cref="TenantOptions"/>:
/// </para>
/// <list type="number">
///   <item>
///     The <c>tenant_id</c> claim of a valid bearer token. This is
///     <b>authoritative</b>: the claim is signed, so for a token-authenticated
///     caller the header / cookie are ignored entirely — otherwise a user of
///     tenant A could simply send <c>X-Tenant-Id: B</c> and operate in another
///     tenant's scope. A valid token that carries no tenant likewise resolves
///     to "no tenant" rather than falling back to a spoofable header.
///   </item>
///   <item>HTTP header (default <c>X-Tenant-Id</c>)</item>
///   <item>Cookie (default <c>va-tenant</c>) — the admin sidebar switcher.</item>
/// </list>
/// <para>
/// Operators driving the admin dashboard authenticate with the Identity cookie,
/// whose principal carries no <c>tenant_id</c> claim, so they keep falling
/// through to the cookie switcher and may switch to any tenant by design.
/// </para>
/// <para>
/// The bearer token is authenticated here explicitly rather than read off
/// <see cref="HttpContext.User"/>, so this middleware keeps working wherever the
/// host places it relative to <c>UseAuthentication</c> (the JWT scheme is not
/// the default one, so it is normally only run during endpoint authorization).
/// The handler caches its result per request, so the token is not validated
/// twice.
/// </para>
/// </remarks>
public sealed class TenantResolverMiddleware(RequestDelegate next, IOptions<TenantOptions> options)
{
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly TenantOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task InvokeAsync(HttpContext context, IAuthenticationSchemeProvider schemeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemeProvider);

        var (tokenAuthenticated, tenantId) = await ResolveFromBearerTokenAsync(context, schemeProvider);

        // Only fall back to client-supplied values when the caller did not
        // present a valid token. A token's signed claim always wins.
        if (!tokenAuthenticated)
        {
            tenantId = ResolveFromRequest(context);
        }

        if (tenantId is not null)
        {
            context.Items[HttpContextTenantContext.TenantIdItemKey] = tenantId;
        }
        else if (_options.RequireTenant)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(
                $"Missing tenant: the '{_options.HeaderName}' header is required.");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Authenticates the bearer token, when one is presented and the scheme is
    /// wired, and reads its tenant claim.
    /// </summary>
    /// <returns>
    /// <c>Authenticated</c> is true when a valid token was presented — in which
    /// case <c>TenantId</c> is the token's tenant, or <c>null</c> meaning the
    /// token is explicitly tenant-less. Both outcomes suppress the header /
    /// cookie fallback.
    /// </returns>
    private static async Task<(bool Authenticated, string? TenantId)> ResolveFromBearerTokenAsync(
        HttpContext context,
        IAuthenticationSchemeProvider schemeProvider)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) ||
            !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        // The scheme only exists when the consumer called AddVisuAuthJwt;
        // asking for a handler that was never registered would throw.
        if (await schemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme) is null)
        {
            return (false, null);
        }

        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!result.Succeeded)
        {
            // Expired / forged / revoked. Nothing trustworthy to bind to; the
            // request will be rejected by authorization anyway if it needs auth.
            return (false, null);
        }

        return (true, result.Principal?.FindFirst(VisuAuthClaimTypes.TenantId)?.Value);
    }

    /// <summary>Header first, then the cookie the admin switcher writes.</summary>
    private string? ResolveFromRequest(HttpContext context)
    {
        if (_options.UseHeader &&
            context.Request.Headers.TryGetValue(_options.HeaderName, out var headerValue))
        {
            var candidate = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        if (_options.UseCookie &&
            context.Request.Cookies.TryGetValue(_options.CookieName, out var cookieValue) &&
            !string.IsNullOrWhiteSpace(cookieValue))
        {
            return cookieValue.Trim();
        }

        return null;
    }
}
