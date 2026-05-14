using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Options;

namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Inserts <see cref="VisuAuthViewLocationExpander"/> at the head of
/// <see cref="RazorViewEngineOptions.ViewLocationExpanders"/> so consumer
/// override paths get checked before the package's own.
/// </summary>
/// <remarks>
/// Registered as <c>IConfigureOptions&lt;RazorViewEngineOptions&gt;</c> instead
/// of mutating the options inline at <c>AddVisuAuth</c> time — this lets the
/// expander resolve <see cref="IOptionsMonitor{T}"/> from DI properly,
/// without building an intermediate service provider.
/// </remarks>
internal sealed class VisuAuthViewLocationConfigure(
    IOptionsMonitor<VisuAuthViewOverrideOptions> overrideOptions)
    : IConfigureOptions<RazorViewEngineOptions>
{
    public void Configure(RazorViewEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Insert at index 0 so the consumer override path is the first
        // location Razor probes. Standard package + host paths follow.
        options.ViewLocationExpanders.Insert(0, new VisuAuthViewLocationExpander(overrideOptions));
    }
}
