namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Theming layer 3 (CLAUDE.md §8.4) — point Razor at a folder in the
/// consumer's project where same-named <c>.cshtml</c> files override
/// VisuAuth's built-in views.
/// </summary>
/// <remarks>
/// Two cooperating mechanisms read this options bag:
///
/// <list type="bullet">
/// <item><description>
/// A <c>VisuAuthViewLocationExpander</c> prepends <c>{Root}/{name}.cshtml</c>
/// and <c>{Root}/Shared/{name}.cshtml</c> to the Razor view-engine search
/// list, so any <c>Html.PartialAsync(...)</c>, <c>return Partial(...)</c>,
/// or layout reference (<c>_Layout</c>, <c>_EndUserLayout</c>) finds the
/// consumer copy first.
/// </description></item>
/// <item><description>
/// A <c>DemoteVisuAuthPagesConvention</c> demotes every Razor Page that
/// lives in the VisuAuth assemblies, so a consumer Razor Page declaring the
/// same <c>@page "/visuauth/login"</c> route automatically wins via the
/// lower-order-wins rule. The consumer page is a regular Razor Page in
/// their host app — no extra registration.
/// </description></item>
/// </list>
///
/// <code>
/// services.AddVisuAuth&lt;ApplicationUser&gt;();
/// services.Configure&lt;VisuAuthViewOverrideOptions&gt;(o =&gt;
/// {
///     o.Root = "/Areas/MyBrand/Views"; // anywhere under the host project
/// });
/// </code>
///
/// The default of <c>/Views/VisuAuth</c> matches the convention named in
/// CLAUDE.md §8.4 and works without any extra <c>Configure</c> call.
/// </remarks>
public sealed class VisuAuthViewOverrideOptions
{
    /// <summary>
    /// Folder (relative to the host project root) where consumer override
    /// views live. Default: <c>/Views/VisuAuth</c>.
    /// </summary>
    public string Root { get; set; } = "/Views/VisuAuth";
}
