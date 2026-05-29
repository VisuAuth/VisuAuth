using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using VisuAuth.EntraCore.Infrastructure;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Supplies the <see cref="GraphServiceClient"/> the Entra stores consume,
/// rebuilding it lazily when the effective <see cref="EntraOptions"/> change.
/// </summary>
/// <remarks>
/// <para>
/// The Graph client is expensive to build and safe to reuse, so it's cached
/// and only rebuilt when a credential-affecting option actually changes
/// (tracked by a fingerprint hash). Reading
/// <see cref="IOptionsMonitor{T}.CurrentValue"/> re-materializes the options —
/// re-running <see cref="EntraDbConfigOverlay"/> — whenever
/// <see cref="EntraConfigChangeSignal"/> fires, so an admin save takes effect
/// on the very next Graph call without a process restart.
/// </para>
/// <para>
/// If a recompute yields invalid options (e.g. the admin cleared a required
/// field) the provider keeps serving the last valid client rather than letting
/// an <see cref="OptionsValidationException"/> surface as a 500 mid-request.
/// </para>
/// </remarks>
public sealed class EntraGraphClientProvider(IOptionsMonitor<EntraOptions> monitor) : IEntraGraphClient, IDisposable
{
    private readonly IOptionsMonitor<EntraOptions> _monitor =
        monitor ?? throw new ArgumentNullException(nameof(monitor));
    private readonly object _gate = new();
    private GraphServiceClient? _client;
    private string? _fingerprint;

    /// <summary>Returns the current Graph client, rebuilding it if the options changed.</summary>
    public GraphServiceClient GetClient()
    {
        EntraOptions options;
        try
        {
            options = _monitor.CurrentValue;
        }
        catch (OptionsValidationException) when (_client is not null)
        {
            // A recomputed config is invalid; keep the last good client until
            // the operator fixes it.
            return _client;
        }

        var fingerprint = Fingerprint(options);
        lock (_gate)
        {
            if (_client is not null && string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal))
            {
                return _client;
            }

            // Don't dispose the previous client here — an in-flight request may
            // still hold it. Rebuilds are rare (an admin save); the old client
            // is released to the GC.
            _client = EntraGraphClientFactory.Create(
                options.TenantId, options.ClientId, options.ClientSecret, options.GraphBaseUrl);
            _fingerprint = fingerprint;
            return _client;
        }
    }

    // SHA-256 over the credential-affecting fields. Hashing keeps the raw
    // ClientSecret out of the long-lived fingerprint field; a newline separator
    // is fine since only the hash is compared.
    private static string Fingerprint(EntraOptions o)
    {
        var raw = string.Concat(o.TenantId, "\n", o.ClientId, "\n", o.ClientSecret, "\n", o.GraphBaseUrl);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public void Dispose() => (_client as IDisposable)?.Dispose();
}
