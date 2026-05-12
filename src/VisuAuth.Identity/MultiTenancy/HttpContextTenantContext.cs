using Microsoft.AspNetCore.Http;

using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// <see cref="ITenantContext"/> backed by <see cref="HttpContext.Items"/>.
/// The resolver middleware writes the tenant id at the start of every request,
/// and this context reads it back on demand.
/// </summary>
/// <remarks>
/// Scoped lifetime: one instance per request. <see cref="IHttpContextAccessor"/>
/// gives us read access to <c>HttpContext.Items</c> from any DI consumer
/// (stores, query filters, etc.).
/// </remarks>
public sealed class HttpContextTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    /// <summary>
    /// Key the resolver middleware writes under in <c>HttpContext.Items</c>.
    /// Public so other components (tests, custom middleware) can interop.
    /// </summary>
    public const string TenantIdItemKey = "VisuAuth.TenantId";

    /// <summary>Companion key for the display name (when supplied by the resolver).</summary>
    public const string TenantDisplayNameItemKey = "VisuAuth.TenantDisplayName";

    private readonly IHttpContextAccessor _accessor =
        httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    /// <inheritdoc />
    public bool IsMultiTenancyEnabled => true;

    /// <inheritdoc />
    public string? CurrentTenantId => _accessor.HttpContext?.Items[TenantIdItemKey] as string;

    /// <inheritdoc />
    public string? CurrentTenantDisplayName =>
        _accessor.HttpContext?.Items[TenantDisplayNameItemKey] as string;
}
