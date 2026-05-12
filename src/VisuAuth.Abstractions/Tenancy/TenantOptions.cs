namespace VisuAuth.Abstractions.Tenancy;

/// <summary>
/// Configuration knobs for multi-tenancy. Bound from <c>IOptions&lt;TenantOptions&gt;</c>
/// at runtime. Subdomain and JWT-claim resolvers are placeholders for now —
/// header resolution is the only source that ships with this PR.
/// </summary>
public sealed class TenantOptions
{
    /// <summary>HTTP header inspected by the resolver. Defaults to <c>X-Tenant-Id</c>.</summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>Read the tenant id from <see cref="HeaderName"/>. On by default.</summary>
    public bool UseHeader { get; set; } = true;

    /// <summary>Cookie name inspected by the resolver. Defaults to <c>va-tenant</c>.</summary>
    public string CookieName { get; set; } = "va-tenant";

    /// <summary>
    /// Read the tenant id from <see cref="CookieName"/>. On by default — the
    /// sidebar switcher uses this for browser flows. Header still takes
    /// precedence when set.
    /// </summary>
    public bool UseCookie { get; set; } = true;

    /// <summary>Reserved for the subdomain resolver (lands in a follow-up PR).</summary>
    public bool UseSubdomain { get; set; }

    /// <summary>Reserved for the JWT-claim resolver (lands with the mobile/JWT PR).</summary>
    public bool UseClaim { get; set; }

    /// <summary>JWT claim name to inspect when <see cref="UseClaim"/> is true.</summary>
    public string ClaimName { get; set; } = "tenant_id";

    /// <summary>
    /// When true, requests without a resolvable tenant id are rejected with 400.
    /// When false, the request proceeds with <c>CurrentTenantId = null</c> — useful
    /// for global admin paths that opt out of tenancy.
    /// </summary>
    public bool RequireTenant { get; set; }
}
