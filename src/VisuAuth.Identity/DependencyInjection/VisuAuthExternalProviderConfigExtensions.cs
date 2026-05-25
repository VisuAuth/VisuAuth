using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;

namespace VisuAuth.Identity.DependencyInjection;

/// <summary>
/// Registration helpers for the EF-backed external-provider configuration
/// store + the dynamic options-overlay path that lets admins edit
/// ClientId / ClientSecret at runtime via <c>/visuauth/admin/external-providers</c>
/// without an app restart.
/// </summary>
public static class VisuAuthExternalProviderConfigExtensions
{
    /// <summary>
    /// Registers <see cref="EfCoreExternalProviderConfigStore"/> against the
    /// consumer's metadata DbContext. Required before any
    /// <see cref="AddVisuAuthDynamicExternalProviderOptions{TOptions}"/>
    /// call and before the admin UI page can read / write.
    /// </summary>
    public static IServiceCollection AddVisuAuthExternalProviderConfigStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // DataProtection lives in the shared framework but the IDataProtectionProvider
        // service is registered by the host (UseEndpoints / AddDataProtection). Most
        // hosts already have it via ASP.NET Core's defaults; we don't AddDataProtection
        // here to avoid clobbering a consumer's persistent key configuration (Key Vault,
        // FileSystem, etc.).
        services.TryAddScoped<IExternalProviderConfigStore, EfCoreExternalProviderConfigStore>();
        return services;
    }

    /// <summary>
    /// Wires the runtime overlay for a single OAuth scheme: the configurator
    /// reads the latest admin-edited <c>ClientId</c>/<c>ClientSecret</c> from
    /// the store and applies them on top of whatever the consumer's
    /// <c>.AddMicrosoftAccount(...)</c> / <c>.AddGoogle(...)</c> / etc. call
    /// set. Saving in the admin UI calls
    /// <see cref="IOptionsMonitorCache{TOptions}.TryRemove"/> for this scheme
    /// so the next sign-in attempt rebuilds options with the fresh values.
    /// </summary>
    /// <typeparam name="TOptions">The provider's options type
    /// (e.g. <c>MicrosoftAccountOptions</c>, <c>GoogleOptions</c>,
    /// <c>AppleAuthenticationOptions</c>, <c>GitHubAuthenticationOptions</c>).
    /// Must derive from <see cref="OAuthOptions"/>.</typeparam>
    /// <param name="schemeName">The authentication scheme name the consumer
    /// passed to <c>.AddXxx(scheme, ...)</c> — typically the provider name
    /// (Microsoft, Google, Apple, GitHub).</param>
    public static IServiceCollection AddVisuAuthDynamicExternalProviderOptions<TOptions>(
        this IServiceCollection services,
        string schemeName)
        where TOptions : OAuthOptions
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);

        // CRITICAL: register under IConfigureOptions<TOptions>, not under
        // IConfigureNamedOptions<TOptions>. OptionsFactory<T> only resolves
        // IEnumerable<IConfigureOptions<T>> in its ctor — entries registered
        // *only* as IConfigureNamedOptions<T> never enter that enumerable
        // and the configurator silently never runs (we hit exactly that bug
        // in 0.2.0-alpha — symptom was "edits in /visuauth/admin/external-providers
        // never reached the auth handler at runtime"). The runtime DOES
        // downcast each setup back to IConfigureNamedOptions<T> per scheme
        // name, so this registration still gets the named-overload call —
        // we just had to register under the broader contract to be visible.
        services.AddSingleton<IConfigureOptions<TOptions>>(sp =>
            new DynamicExternalProviderOptionsConfigurator<TOptions>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                schemeName));

        // Each call adds one record; the invalidator collects them via the
        // IEnumerable<...> injection to map scheme → TOptions when the
        // admin save handler calls Invalidate, AND the registry exposes the
        // same set as the public IExternalProviderRegistry so the admin
        // page knows which providers are actually wired at runtime.
        services.AddSingleton(new ExternalProviderSchemeRegistration(schemeName, typeof(TOptions)));
        services.TryAddSingleton<IExternalProviderOptionsCacheInvalidator, ExternalProviderOptionsCacheInvalidator>();
        services.TryAddSingleton<IExternalProviderRegistry, ExternalProviderRegistry>();
        // Registered under BOTH the concrete type (the configurator calls
        // .Capture on it) and the public interface (the admin UI calls .Get
        // through it). Two registrations of the same singleton instance —
        // the factory ensures only one is created.
        services.TryAddSingleton<ExternalProviderStaticConfigSnapshot>();
        services.TryAddSingleton<IExternalProviderStaticConfigSnapshot>(
            sp => sp.GetRequiredService<ExternalProviderStaticConfigSnapshot>());
        return services;
    }
}
