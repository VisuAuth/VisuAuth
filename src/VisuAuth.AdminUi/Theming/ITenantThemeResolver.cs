namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Theming layer 4 (CLAUDE.md §8.4) — return a per-tenant
/// <see cref="VisuAuthTheme"/> at request time. The
/// <c>&lt;va-theme-style /&gt;</c> tag helper calls this on every render
/// and overlays the result on top of the global theme set via
/// <c>services.Configure&lt;VisuAuthTheme&gt;(...)</c>, so a resolver can
/// override only the properties it cares about (e.g. swap
/// <see cref="VisuAuthTheme.Primary"/> per tenant while keeping the
/// shared neutrals).
/// </summary>
/// <remarks>
/// The default registration is <see cref="NoOpTenantThemeResolver"/>,
/// which always returns <see langword="null"/> — single-tenant
/// deployments and consumers who never opt in pay nothing. Consumers
/// who want per-tenant branding register their own implementation:
///
/// <code>
/// services.AddSingleton&lt;ITenantThemeResolver, MyTenantThemeResolver&gt;();
/// </code>
///
/// (Use <c>AddSingleton</c> for a stateless lookup, <c>AddScoped</c>
/// when the resolver depends on per-request services like a DbContext.
/// VisuAuth itself resolves the contract through DI on every render so
/// either lifetime works.)
/// </remarks>
public interface ITenantThemeResolver
{
    /// <summary>
    /// Returns the theme overrides for <paramref name="tenantId"/>, or
    /// <see langword="null"/> when no per-tenant override applies. A
    /// returned theme is merged on top of the global
    /// <c>IOptions&lt;VisuAuthTheme&gt;</c>; properties left null in
    /// both fall through to the defaults declared in <c>visuauth.css</c>.
    /// </summary>
    /// <param name="tenantId">
    /// The current tenant's id from <c>ITenantContext.CurrentTenantId</c>,
    /// or <see langword="null"/> when multi-tenancy is off / unscoped.
    /// </param>
    Task<VisuAuthTheme?> ResolveAsync(string? tenantId, CancellationToken ct = default);
}
