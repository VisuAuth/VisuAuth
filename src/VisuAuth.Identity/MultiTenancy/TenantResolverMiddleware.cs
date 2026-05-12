using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Resolves the current tenant from the request and stashes it on
/// <see cref="HttpContext.Items"/> for downstream consumers (the
/// <see cref="HttpContextTenantContext"/>, query filters, the save-changes
/// interceptor, etc.).
/// </summary>
/// <remarks>
/// Resolution order, when enabled in <see cref="TenantOptions"/>:
/// <list type="number">
///   <item>HTTP header (default <c>X-Tenant-Id</c>)</item>
///   <item>Subdomain (placeholder — lands in a follow-up PR)</item>
///   <item>JWT claim (placeholder — lands with the mobile / JWT PR)</item>
/// </list>
/// </remarks>
public sealed class TenantResolverMiddleware(RequestDelegate next, IOptions<TenantOptions> options)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly TenantOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? tenantId = null;

        if (_options.UseHeader &&
            context.Request.Headers.TryGetValue(_options.HeaderName, out var headerValue))
        {
            var candidate = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                tenantId = candidate.Trim();
            }
        }

        // Subdomain and claim resolvers are wired in follow-up PRs.

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
}
