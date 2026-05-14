using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Razor view-engine hook for theming layer 3 (CLAUDE.md §8.4).
/// Prepends the consumer's override folders to every view-location
/// search so a same-named <c>.cshtml</c> dropped in
/// <c>{Root}/{name}.cshtml</c> wins over the package's built-in copy
/// without any forking. When a per-tenant override resolver is
/// registered (layer 4 wiring), tenant-specific roots take precedence
/// over the global root for the request's current tenant.
/// </summary>
/// <remarks>
/// Reads <see cref="VisuAuthViewOverrideOptions"/> on every render
/// through <see cref="IOptionsMonitor{T}"/> so re-configuring at
/// runtime takes effect on the next request — no service-locator
/// hack at startup. Per-tenant scoped services
/// (<see cref="ITenantContext"/>, <see cref="ITenantViewOverrideResolver"/>)
/// are pulled from <c>HttpContext.RequestServices</c> on every call so
/// this singleton expander can safely depend on them.
/// </remarks>
internal sealed class VisuAuthViewLocationExpander(
    IOptionsMonitor<VisuAuthViewOverrideOptions> options)
    : IViewLocationExpander
{
    private const string GlobalRootKey = "visuauth-view-override-root";
    private const string TenantRootKey = "visuauth-view-override-tenant-root";

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Stash the live global root in the context's value bag so
        // Razor's view-location cache key changes whenever the
        // consumer reconfigures. Without this, a stale cached lookup
        // would shadow a new root.
        context.Values[GlobalRootKey] = Normalize(options.CurrentValue.Root);
        // Same story for the per-tenant root: the cache key must
        // include it so Razor doesn't serve tenant A's swapped
        // _UsersTable.cshtml to tenant B on the next request.
        context.Values[TenantRootKey] = ResolveTenantRoot(context);
    }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(viewLocations);

        context.Values.TryGetValue(GlobalRootKey, out var globalRoot);
        context.Values.TryGetValue(TenantRootKey, out var tenantRoot);

        var hasGlobal = !string.IsNullOrEmpty(globalRoot);
        var hasTenant = !string.IsNullOrEmpty(tenantRoot);
        if (!hasGlobal && !hasTenant)
        {
            return viewLocations;
        }

        // Probe order — most specific first, so the first match wins:
        //   1. {tenantRoot}/{0}.cshtml          (per-tenant override)
        //   2. {tenantRoot}/Shared/{0}.cshtml   (per-tenant layout)
        //   3. {globalRoot}/{0}.cshtml          (consumer-wide override)
        //   4. {globalRoot}/Shared/{0}.cshtml   (consumer-wide layout)
        //   5. ...the package defaults already in viewLocations
        var prepended = new List<string>(capacity: 4);
        if (hasTenant)
        {
            prepended.Add($"{tenantRoot}/{{0}}.cshtml");
            prepended.Add($"{tenantRoot}/Shared/{{0}}.cshtml");
        }
        if (hasGlobal)
        {
            prepended.Add($"{globalRoot}/{{0}}.cshtml");
            prepended.Add($"{globalRoot}/Shared/{{0}}.cshtml");
        }
        return [.. prepended, .. viewLocations];
    }

    /// <summary>
    /// Bridges this singleton expander to the per-request
    /// <see cref="ITenantContext"/> + <see cref="ITenantViewOverrideResolver"/>
    /// via <c>HttpContext.RequestServices</c>. Returns an empty string
    /// (cache-key-stable, expander-skips-the-slot) on every "no
    /// per-tenant override" branch.
    /// </summary>
    private static string ResolveTenantRoot(ViewLocationExpanderContext context)
    {
        // ActionContext / HttpContext / RequestServices are all nullable in
        // theory — and in unit-test code paths they can each end up null
        // because nothing wires them up. Treat any missing piece as "no
        // per-tenant override" so the expander degrades to the layer-3
        // (global-only) behaviour instead of throwing.
        var services = context.ActionContext?.HttpContext?.RequestServices;
        if (services is null)
        {
            return string.Empty;
        }

        var tenantContext = services.GetService<ITenantContext>();
        if (tenantContext is null || !tenantContext.IsMultiTenancyEnabled)
        {
            return string.Empty;
        }

        var resolver = services.GetService<ITenantViewOverrideResolver>();
        var raw = resolver?.ResolveOverrideRoot(tenantContext.CurrentTenantId);
        return Normalize(raw);
    }

    private static string Normalize(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }
        // Razor view paths are app-root-relative and use forward slashes.
        // Trim trailing separators so we never emit "//{0}.cshtml".
        var trimmed = root.Replace('\\', '/').TrimEnd('/');
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
