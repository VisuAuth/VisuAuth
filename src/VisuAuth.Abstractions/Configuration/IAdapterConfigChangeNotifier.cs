namespace VisuAuth.Abstractions.Configuration;

/// <summary>
/// Lets the admin config page signal that an adapter's stored settings changed,
/// without the AdminUi package referencing the adapter. The adapter registers
/// an implementation that triggers its own options re-materialization (so a
/// save takes effect without a restart); the page resolves
/// <see cref="IEnumerable{T}"/> of these and notifies the one whose
/// <see cref="Adapter"/> matches the saved adapter.
/// </summary>
public interface IAdapterConfigChangeNotifier
{
    /// <summary>Adapter key this notifier handles (e.g. <c>"Entra"</c>).</summary>
    string Adapter { get; }

    /// <summary>Signals that the adapter's configuration was just saved.</summary>
    void NotifyChanged();
}
