using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// Singleton implementation of <see cref="IExternalProviderRegistry"/> backed
/// by the <see cref="ExternalProviderSchemeRegistration"/> records that each
/// <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c> call adds
/// to DI. Resolution is O(1) on the scheme name through a frozen dictionary
/// so the admin page can hit it for every render without a measurable cost.
/// </summary>
internal sealed class ExternalProviderRegistry : IExternalProviderRegistry
{
    private readonly Dictionary<string, ExternalProviderSchemeRegistration> _bySchema;

    public ExternalProviderRegistry(IEnumerable<ExternalProviderSchemeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        // Materialise once — order matters so the admin renders providers in
        // the registration order rather than scheme-alphabetical (matches the
        // sample's appsettings ordering).
        Registrations = [.. registrations];
        _bySchema = Registrations.ToDictionary(r => r.Scheme, StringComparer.Ordinal);
    }

    public IReadOnlyList<ExternalProviderSchemeRegistration> Registrations { get; }

    public bool IsRegistered(string scheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        return _bySchema.ContainsKey(scheme);
    }
}
