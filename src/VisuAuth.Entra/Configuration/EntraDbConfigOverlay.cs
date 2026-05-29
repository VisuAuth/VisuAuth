using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Configuration;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Overlays admin-edited <see cref="IAdapterConfigStore"/> values on top of the
/// <see cref="EntraOptions"/> bound from code / appsettings / user-secrets.
/// Registered as an <see cref="IConfigureOptions{TOptions}"/> <b>after</b> the
/// consumer's bind step, so a DB override wins; a key with no DB override keeps
/// the static value. Re-runs whenever <see cref="IOptionsMonitor{TOptions}"/>
/// re-materializes the options (triggered by <see cref="EntraConfigChangeSignal"/>
/// on save), which is what makes edits take effect without a restart.
/// </summary>
/// <remarks>
/// Resolves the scoped store through an <see cref="IServiceScopeFactory"/>
/// because this configurator is itself a singleton in the options pipeline.
/// A missing store (no <c>AddVisuAuthAdapterConfigStore</c>) or a transient DB
/// failure leaves the static configuration untouched — the adapter degrades to
/// its code/appsettings values rather than breaking startup.
/// </remarks>
public sealed class EntraDbConfigOverlay(
    IServiceScopeFactory scopeFactory,
    EntraConfigStaticSnapshot snapshot) : IConfigureOptions<EntraOptions>
{
    private readonly IServiceScopeFactory _scopeFactory =
        scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly EntraConfigStaticSnapshot _snapshot =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public void Configure(EntraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Capture the static (pre-overlay) values for the admin source badges
        // before we mutate anything.
        _snapshot.CaptureOnce(options);

        IReadOnlyDictionary<string, string> overrides;
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IAdapterConfigStore>();
        if (store is null)
        {
            return;
        }
        try
        {
            overrides = store.GetResolvedAsync(EntraAdapterConfigKeys.Adapter).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DB unavailable (e.g. migrations not yet applied) — keep the
            // static configuration rather than failing options materialization.
            return;
        }

        Apply(options, overrides);
    }

    private static void Apply(EntraOptions options, IReadOnlyDictionary<string, string> overrides)
    {
        if (TryGet(overrides, EntraAdapterConfigKeys.TenantId, out var tenantId))
        {
            options.TenantId = tenantId;
        }
        if (TryGet(overrides, EntraAdapterConfigKeys.ClientId, out var clientId))
        {
            options.ClientId = clientId;
        }
        if (TryGet(overrides, EntraAdapterConfigKeys.ClientSecret, out var clientSecret))
        {
            options.ClientSecret = clientSecret;
        }
        if (TryGet(overrides, EntraAdapterConfigKeys.AppRoleResourceId, out var appRoleResourceId))
        {
            options.AppRoleResourceId = appRoleResourceId;
        }
        if (TryGet(overrides, EntraAdapterConfigKeys.GraphBaseUrl, out var graphBaseUrl))
        {
            options.GraphBaseUrl = graphBaseUrl;
        }
        if (TryGet(overrides, EntraAdapterConfigKeys.DefaultEmailDomain, out var defaultEmailDomain))
        {
            options.DefaultEmailDomain = defaultEmailDomain;
        }
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> overrides, string key, out string value)
    {
        if (overrides.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            value = v;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
