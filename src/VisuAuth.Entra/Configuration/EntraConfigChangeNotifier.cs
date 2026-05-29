using VisuAuth.Abstractions.Configuration;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Adapter-agnostic hook the admin config page calls after saving the Entra
/// config — fires <see cref="EntraConfigChangeSignal"/> so the options monitor
/// recomputes and the Graph client rebuilds on the next call.
/// </summary>
public sealed class EntraConfigChangeNotifier(EntraConfigChangeSignal signal) : IAdapterConfigChangeNotifier
{
    private readonly EntraConfigChangeSignal _signal =
        signal ?? throw new ArgumentNullException(nameof(signal));

    public string Adapter => EntraAdapterConfigKeys.Adapter;

    public void NotifyChanged() => _signal.Notify();
}
