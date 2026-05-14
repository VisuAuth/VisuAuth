using VisuAuth.AdminUi.Theming;

namespace Sample.WebApp.Theming;

/// <summary>
/// Demonstrates per-tenant view overrides (CLAUDE.md §8.4 layers 3+4).
/// Maps the seeded <c>acme</c> tenant to a folder that ships an
/// alternate <c>_UsersTable.cshtml</c> so flipping the sidebar tenant
/// switcher to <c>acme</c> visibly swaps the admin users table for
/// acme's branded version. Other tenants fall through to the global
/// <c>/Views/VisuAuth/</c> override (which itself shows a "sample app
/// override" banner).
/// </summary>
internal sealed class SampleTenantViewOverrideResolver : ITenantViewOverrideResolver
{
    public string? ResolveOverrideRoot(string? tenantId) => tenantId switch
    {
        "acme" => "/Views/VisuAuth/Tenants/acme",
        _ => null,
    };
}
