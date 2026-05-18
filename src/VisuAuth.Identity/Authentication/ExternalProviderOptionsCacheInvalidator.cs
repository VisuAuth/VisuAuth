using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.Identity.Authentication;

// The ExternalProviderSchemeRegistration record itself lives in
// VisuAuth.Abstractions alongside IExternalProviderRegistry — both the
// invalidator and the AdminUi need that type, and Abstractions is the only
// project they both depend on.

internal sealed class ExternalProviderOptionsCacheInvalidator(
    IServiceProvider services,
    IEnumerable<ExternalProviderSchemeRegistration> registrations) : IExternalProviderOptionsCacheInvalidator
{
    private readonly IServiceProvider _services = services;
    private readonly Dictionary<string, Type> _schemes = registrations.ToDictionary(
        r => r.Scheme,
        r => r.OptionsType,
        StringComparer.Ordinal);

    public bool Invalidate(string scheme)
    {
        if (!_schemes.TryGetValue(scheme, out var optionsType))
        {
            return false;
        }

        // IOptionsMonitorCache<T> is generic — resolve via reflection because
        // T isn't known at compile time here.
        var cacheType = typeof(IOptionsMonitorCache<>).MakeGenericType(optionsType);
        var cache = _services.GetRequiredService(cacheType);
        var tryRemove = cacheType.GetMethod(
            nameof(IOptionsMonitorCache<object>.TryRemove),
            [typeof(string)])!;
        _ = tryRemove.Invoke(cache, [scheme]);
        return true;
    }
}
