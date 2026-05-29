using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Process-wide signal that the admin-edited Entra configuration changed.
/// The admin save handler calls <see cref="Notify"/>; the
/// <see cref="EntraConfigChangeTokenSource"/> hands the current token to the
/// options system so <see cref="IOptionsMonitor{TOptions}"/> re-materializes
/// <see cref="EntraOptions"/> (re-running the DB overlay) on the next read —
/// which lets <see cref="EntraGraphClientProvider"/> rebuild the Graph client
/// without an app restart.
/// </summary>
public sealed class EntraConfigChangeSignal : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _cts = new();

    /// <summary>Current change token; fires once when <see cref="Notify"/> is called.</summary>
    public IChangeToken GetChangeToken()
    {
        lock (_gate)
        {
            return new CancellationChangeToken(_cts.Token);
        }
    }

    /// <summary>Signals a configuration change and rotates the token.</summary>
    public void Notify()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            old = _cts;
            _cts = new CancellationTokenSource();
        }
        // Cancel outside the lock so subscribers' callbacks don't run under it.
        old.Cancel();
        old.Dispose();
    }

    /// <summary>Disposes the current token source on container teardown.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _cts.Dispose();
        }
    }
}

/// <summary>
/// Bridges <see cref="EntraConfigChangeSignal"/> into the options system as the
/// change-token source for <see cref="EntraOptions"/>.
/// </summary>
public sealed class EntraConfigChangeTokenSource(EntraConfigChangeSignal signal)
    : IOptionsChangeTokenSource<EntraOptions>
{
    private readonly EntraConfigChangeSignal _signal =
        signal ?? throw new ArgumentNullException(nameof(signal));

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => _signal.GetChangeToken();
}
