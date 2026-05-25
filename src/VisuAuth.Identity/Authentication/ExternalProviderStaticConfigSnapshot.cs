using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// Singleton snapshot of "what was in the options BEFORE VisuAuth's overlay
/// applied" — populated by <see cref="DynamicExternalProviderOptionsConfigurator{TOptions}"/>
/// at the top of each <c>Configure</c> call.
/// </summary>
/// <remarks>
/// Warmup is lazy: the first <see cref="Get"/> call triggers
/// <see cref="EnsureWarmedUp"/>, which resolves <c>IOptionsMonitor&lt;TOptions&gt;</c>
/// reflectively for every registered scheme and asks it for the options.
/// That call materialises the options, which in turn runs the configurator,
/// which in turn calls <see cref="Capture"/> — populating the snapshot.
/// Idempotent: subsequent calls hit a fast path with the warmed-up flag set.
/// </remarks>
internal sealed class ExternalProviderStaticConfigSnapshot(
    IServiceProvider services,
    IExternalProviderRegistry registry) : IExternalProviderStaticConfigSnapshot
{
    private readonly ConcurrentDictionary<string, StaticProviderConfigView> _captures = new(StringComparer.Ordinal);
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly IExternalProviderRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private int _warmedUp;

    /// <summary>
    /// Invoked by <see cref="DynamicExternalProviderOptionsConfigurator{TOptions}"/>
    /// before its overlay touches <c>options.ClientId/ClientSecret</c>.
    /// Last-write-wins is fine — repeated configurator runs see the same
    /// static state because the upstream <c>IConfigureOptions</c> are
    /// idempotent.
    /// </summary>
    internal void Capture(string scheme, StaticProviderConfigView view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentNullException.ThrowIfNull(view);
        _captures[scheme] = view;
    }

    /// <inheritdoc />
    public StaticProviderConfigView? GetForScheme(string scheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        EnsureWarmedUp();
        return _captures.TryGetValue(scheme, out var view) ? view : null;
    }

    /// <summary>
    /// Forces every registered scheme's options to materialise — which
    /// runs the configurator chain and therefore populates the snapshot.
    /// Uses <see cref="IOptionsFactory{TOptions}"/>'s <c>Create</c> path
    /// (not <see cref="IOptionsMonitor{TOptions}"/>) because Monitor caches
    /// per-name and returns the cached instance without re-running the
    /// configurator chain on subsequent calls — that would miss the
    /// snapshot when the options have already been materialised by an
    /// earlier auth challenge or DI resolution. <c>Create</c> always runs
    /// the full pipeline and discards the result, which is exactly what we
    /// need to drive the snapshot.
    /// <para>
    /// Wrapped in try/catch per-scheme because a single bad handler
    /// shouldn't blank the snapshot for the others.
    /// <see cref="Interlocked.CompareExchange{T}"/> guards against
    /// concurrent first-call races without taking a lock.
    /// </para>
    /// </summary>
    private void EnsureWarmedUp()
    {
        if (Interlocked.CompareExchange(ref _warmedUp, 1, 0) != 0)
        {
            return;
        }

        foreach (var reg in _registry.Registrations)
        {
            try
            {
                var factoryType = typeof(IOptionsFactory<>).MakeGenericType(reg.OptionsType);
                var factory = _services.GetService(factoryType);
                if (factory is null)
                {
                    continue;
                }
                var createMethod = factoryType.GetMethod(
                    nameof(IOptionsFactory<object>.Create),
                    [typeof(string)]);
                _ = createMethod!.Invoke(factory, [reg.Scheme]);
            }
#pragma warning disable CA1031 // Snapshot warmup is best-effort — surfacing a misbehaving handler here would blank the admin UI for every other scheme.
            catch
            {
                // Intentionally swallow: the snapshot will simply be missing
                // for this scheme, and the UI shows it as "no static config"
                // which is honest.
            }
#pragma warning restore CA1031
        }
    }
}
