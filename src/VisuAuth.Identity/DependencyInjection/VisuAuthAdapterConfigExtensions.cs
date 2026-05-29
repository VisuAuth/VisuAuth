using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuAuth.Abstractions.Configuration;
using VisuAuth.Identity.Configuration;

namespace VisuAuth.Identity.DependencyInjection;

/// <summary>
/// Registration helper for the EF-backed adapter-configuration store that lets
/// admins edit a backend adapter's settings (e.g. the Entra adapter's
/// TenantId / ClientId / ClientSecret) at runtime via
/// <c>/visuauth/admin/entra-config</c>, persisting them to the database.
/// </summary>
public static class VisuAuthAdapterConfigExtensions
{
    /// <summary>
    /// Registers <see cref="EfCoreAdapterConfigStore"/> against the consumer's
    /// metadata DbContext. Required before the admin config page can read /
    /// write and before an adapter's DB-overlay configurator can resolve
    /// overrides. The adapter overlay itself is wired by the adapter package
    /// (e.g. <c>AddVisuAuthEntraDbConfig()</c>).
    /// </summary>
    public static IServiceCollection AddVisuAuthAdapterConfigStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The host owns IDataProtectionProvider (ASP.NET Core registers it by
        // default); we don't AddDataProtection here so a consumer's persistent
        // key configuration (Key Vault, FileSystem, …) is never clobbered.
        services.TryAddScoped<IAdapterConfigStore, EfCoreAdapterConfigStore>();
        return services;
    }
}
