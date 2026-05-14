using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Options;

namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Razor view-engine hook for theming layer 3 (CLAUDE.md §8.4). Prepends
/// the consumer's override folder to every view-location search so a
/// same-named <c>.cshtml</c> dropped in <c>{Root}/{name}.cshtml</c> wins
/// over the package's built-in copy without any forking.
/// </summary>
/// <remarks>
/// Reads <see cref="VisuAuthViewOverrideOptions"/> on every render through
/// <see cref="IOptionsMonitor{T}"/> so re-configuring at runtime takes
/// effect on the next request — no service-locator hack at startup.
/// </remarks>
internal sealed class VisuAuthViewLocationExpander(
    IOptionsMonitor<VisuAuthViewOverrideOptions> options)
    : IViewLocationExpander
{
    private const string CacheKey = "visuauth-view-override-root";

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Stash the live root in the context's value bag so Razor's view
        // location cache key changes whenever the consumer reconfigures.
        // Without this, a stale cached lookup would shadow a new root.
        context.Values[CacheKey] = Normalize(options.CurrentValue.Root);
    }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(viewLocations);

        if (!context.Values.TryGetValue(CacheKey, out var root) || string.IsNullOrEmpty(root))
        {
            return viewLocations;
        }

        // Two override slots:
        //   {Root}/{0}.cshtml         — drop a sibling next to your own pages
        //   {Root}/Shared/{0}.cshtml  — mirrors Razor's own /Shared convention
        //                                so _Layout / _EndUserLayout work too.
        return
        [
            $"{root}/{{0}}.cshtml",
            $"{root}/Shared/{{0}}.cshtml",
            .. viewLocations,
        ];
    }

    private static string Normalize(string root)
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
