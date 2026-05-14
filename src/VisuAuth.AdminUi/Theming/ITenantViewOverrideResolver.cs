namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Per-tenant leg of theming layer 3 (CLAUDE.md §8.4) — return a
/// folder where Razor should look for tenant-specific override
/// <c>.cshtml</c> files BEFORE the global
/// <see cref="VisuAuthViewOverrideOptions.Root"/> and the package
/// defaults. A tenant resolver lets the consumer ship distinct
/// branding markup per tenant (e.g. acme's <c>_UsersTable.cshtml</c>
/// vs globex's) without forking the package.
/// </summary>
/// <remarks>
/// The contract is <strong>synchronous</strong> on purpose — Razor's
/// <see cref="Microsoft.AspNetCore.Mvc.Razor.IViewLocationExpander"/>
/// pipeline is sync, with no async escape hatch. Per-tenant override
/// roots are typically static config (an in-memory map, an options
/// blob), so sync fits naturally. Resolvers that need to hit a
/// database should cache results in <c>IMemoryCache</c> behind the
/// scenes; the expander asks on every render and a cache miss would
/// be felt instantly.
///
/// The default registration is <see cref="NoOpTenantViewOverrideResolver"/>,
/// which always returns <see langword="null"/> — single-tenant
/// deployments and consumers who never opt in pay nothing. Consumers
/// who want per-tenant overrides register their own implementation:
///
/// <code>
/// services.AddSingleton&lt;ITenantViewOverrideResolver, MyResolver&gt;();
/// </code>
///
/// (Use <c>AddSingleton</c> for a stateless lookup, <c>AddScoped</c>
/// when the resolver depends on per-request services. The expander
/// resolves the contract through <c>HttpContext.RequestServices</c>
/// on every render so either lifetime works.)
/// </remarks>
public interface ITenantViewOverrideResolver
{
    /// <summary>
    /// Returns the override root for <paramref name="tenantId"/>, or
    /// <see langword="null"/> when no per-tenant override applies.
    /// The expander will probe <c>{root}/{name}.cshtml</c> and
    /// <c>{root}/Shared/{name}.cshtml</c> first, then fall through to
    /// the global <see cref="VisuAuthViewOverrideOptions.Root"/> and
    /// finally the package's built-in views.
    /// </summary>
    /// <param name="tenantId">
    /// The current tenant's id from <c>ITenantContext.CurrentTenantId</c>,
    /// or <see langword="null"/> when multi-tenancy is off / unscoped.
    /// </param>
    /// <returns>
    /// An app-root-relative path (e.g. <c>/Views/VisuAuth/Tenants/acme</c>),
    /// or <see langword="null"/> to skip the per-tenant slot. Same
    /// normalisation as <see cref="VisuAuthViewOverrideOptions.Root"/>
    /// applies — leading slash optional, trailing slash trimmed.
    /// </returns>
    string? ResolveOverrideRoot(string? tenantId);
}
