using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Razor.Hosting;

namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Whole-page leg of theming layer 3 (CLAUDE.md §8.4). Demotes every
/// Razor Page that lives in a given VisuAuth.* assembly by setting its
/// <see cref="Microsoft.AspNetCore.Mvc.Routing.AttributeRouteModel.Order"/>
/// to a high value, so a consumer Razor Page in the host app declaring the
/// same <c>@page "/visuauth/login"</c> route automatically wins via the
/// lower-order-wins rule.
/// </summary>
/// <remarks>
/// The consumer page is a plain Razor Page in their host project — no
/// extra registration or marker attribute. Without this convention, two
/// pages claiming the same route would throw <c>AmbiguousMatchException</c>
/// at runtime.
///
/// Each VisuAuth UI package (AdminUi, EndUserUi) registers ONE instance of
/// this convention with its own assembly via <c>TryAddEnumerable</c>. A
/// consumer using only AdminUi or only EndUserUi gets just the relevant
/// demotion; using both is additive — the conventions don't conflict.
/// </remarks>
public sealed class DemoteVisuAuthPagesConvention(Assembly visuauthAssembly)
    : IPageRouteModelConvention
{
    // The exact value does not matter — anything above 0 (the default for
    // consumer pages) will lose to a consumer override. Using a known
    // sentinel makes the demotion easy to spot when debugging routing.
    public const int OverridableOrder = 1000;

    /// <summary>
    /// Lets idempotent registration paths (calling <c>AddVisuAuthAdminUi</c>
    /// twice through transitive references) detect a convention already
    /// targeting this assembly without comparing private state.
    /// </summary>
    public bool OwnsAssembly(Assembly assembly) => visuauthAssembly == assembly;

    public void Apply(PageRouteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // The Razor SDK stores the originating RazorCompiledItem in the
        // route model's Properties bag, keyed by the type itself. Its
        // Type.Assembly is the assembly that compiled the .cshtml — the
        // only reliable way to tell our pages apart from a consumer page
        // that happens to share the same RelativePath.
        if (!model.Properties.TryGetValue(typeof(RazorCompiledItem), out var raw)
            || raw is not RazorCompiledItem item
            || item.Type.Assembly != visuauthAssembly)
        {
            return;
        }

        foreach (var selector in model.Selectors)
        {
            // Pages without an attribute route never collide, so skip.
            if (selector.AttributeRouteModel is { } route)
            {
                route.Order = OverridableOrder;
            }
        }
    }
}
