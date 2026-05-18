using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// Generic <see cref="IConfigureNamedOptions{TOptions}"/> that overlays
/// admin-managed credentials from <see cref="IExternalProviderConfigStore"/>
/// on top of whatever the consumer's <c>AddXxx(...)</c> call set. Works for
/// any OAuth-based handler (Microsoft, Google, Apple, GitHub, …) because
/// <see cref="OAuthOptions.ClientId"/> and
/// <see cref="OAuthOptions.ClientSecret"/> live on the base class.
/// </summary>
/// <remarks>
/// Registered via <c>services.AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;(scheme)</c>.
/// The configurator is singleton (matches the options factory lifetime),
/// so it grabs <see cref="IServiceScopeFactory"/> and opens a short scope
/// per Configure call to reach the scoped store. Configure runs once per
/// scheme until <see cref="IOptionsMonitorCache{TOptions}.TryRemove"/> is
/// called by the admin save handler — that's the trigger that flips the
/// new credentials into effect without an app restart.
/// </remarks>
public sealed class DynamicExternalProviderOptionsConfigurator<TOptions>
    : IConfigureNamedOptions<TOptions>
    where TOptions : OAuthOptions
{
    /// <summary>
    /// Placeholder we drop into <c>ClientId</c> / <c>ClientSecret</c> when
    /// neither the consumer's static config nor the dynamic store supplied
    /// a value, so <see cref="OAuthOptions.Validate"/> (which throws on
    /// empty) doesn't bring the whole login page down at first render.
    /// </summary>
    /// <remarks>
    /// Picked to read as obviously fake in logs / browser dev tools. OAuth
    /// challenges against a scheme that ends up with this sentinel still
    /// fail (the provider rejects the bogus ClientId), but the rest of the
    /// app keeps serving — admins discover the misconfiguration via the
    /// failed challenge instead of via every page crashing.
    /// </remarks>
    internal const string NotConfiguredSentinel = "VisuAuth.NotConfigured";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _schemeName;

    public DynamicExternalProviderOptionsConfigurator(
        IServiceScopeFactory scopeFactory,
        string schemeName)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        _schemeName = schemeName;
    }

    /// <inheritdoc />
    public void Configure(string? name, TOptions options)
    {
        // IConfigureNamedOptions fires for EVERY scheme that uses TOptions
        // — even ones we don't manage. Guard so we only touch the scheme
        // this configurator was registered for.
        if (!string.Equals(name, _schemeName, StringComparison.Ordinal))
        {
            return;
        }
        CaptureStaticSnapshot(options);
        Apply(options);
        EnsureValidatable(options);
    }

    /// <inheritdoc />
    public void Configure(TOptions options) => Configure(Options.DefaultName, options);

    /// <summary>
    /// Records what the options arrived with — the values the consumer's
    /// static <c>AddXxx(o =&gt; ...)</c> / <c>appsettings</c> / user-secrets
    /// path produced, before our overlay had a chance to replace them. The
    /// snapshot lets the admin UI show a "from code" badge next to fields
    /// that have a static origin so the operator knows whether deleting the
    /// DB row will fall back to a working configuration or to nothing.
    /// </summary>
    private void CaptureStaticSnapshot(TOptions options)
    {
        using var scope = _scopeFactory.CreateScope();
        // Concrete type (not the interface) so the configurator can call the
        // internal Capture method — the abstraction stays read-only for
        // outside consumers.
        var snapshot = scope.ServiceProvider.GetService<ExternalProviderStaticConfigSnapshot>();
        snapshot?.Capture(
            _schemeName,
            new StaticProviderConfigView(
                ClientId: string.IsNullOrWhiteSpace(options.ClientId) ? null : options.ClientId,
                HasClientSecret: !string.IsNullOrEmpty(options.ClientSecret)));
    }

    private void Apply(TOptions options)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IExternalProviderConfigStore>();
        if (store is null)
        {
            // No store registered — keep whatever the consumer's AddXxx
            // call set on the options. This makes the dynamic-config layer
            // strictly opt-in.
            return;
        }

        // Tenant-aware lookup is Phase 1.5; for now use the global config row.
        var config = store.GetAsync(_schemeName, tenantId: null).GetAwaiter().GetResult();
        if (config is null || !config.IsEnabled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.ClientId))
        {
            options.ClientId = config.ClientId;
        }

        if (config.HasClientSecret)
        {
            var plain = store.GetClientSecretAsync(_schemeName, tenantId: null).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(plain))
            {
                options.ClientSecret = plain;
            }
        }
    }

    /// <summary>
    /// Last resort to keep <see cref="OAuthOptions.Validate"/> happy when
    /// neither the consumer's static config nor the dynamic store supplied
    /// credentials. Runs AFTER <see cref="CaptureStaticSnapshot"/> so the
    /// snapshot still sees the genuine "nothing here" state (empty/null)
    /// and the admin UI's "from code" badge stays honest. Without this
    /// fallback, registering a scheme that nobody filled in would crash
    /// every page that materialises the options.
    /// </summary>
    private static void EnsureValidatable(TOptions options)
    {
        if (string.IsNullOrEmpty(options.ClientId))
        {
            options.ClientId = NotConfiguredSentinel;
        }
        if (string.IsNullOrEmpty(options.ClientSecret))
        {
            options.ClientSecret = NotConfiguredSentinel;
        }
    }
}
