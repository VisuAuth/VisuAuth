using System.Collections.Concurrent;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Captures the Entra options as they stood <b>before</b> the DB overlay ran —
/// i.e. the values bound from <c>IConfiguration</c> / the configure lambda /
/// user-secrets. The admin config page reads this to show a "From code" source
/// badge (and the code value for non-secret keys) alongside the store's
/// "From DB" badge. Secret values are recorded as presence-only.
/// </summary>
public sealed class EntraConfigStaticSnapshot
{
    private readonly ConcurrentDictionary<string, string?> _values = new(StringComparer.Ordinal);
    private int _captured;

    /// <summary>
    /// Records the static (pre-overlay) values once. Subsequent calls are
    /// ignored — the static layer is immutable for the process lifetime.
    /// </summary>
    internal void CaptureOnce(EntraOptions options)
    {
        if (Interlocked.CompareExchange(ref _captured, 1, 0) != 0)
        {
            return;
        }

        Set(EntraAdapterConfigKeys.TenantId, options.TenantId);
        Set(EntraAdapterConfigKeys.ClientId, options.ClientId);
        // ClientSecret is a secret — record presence only, never the value.
        _values[EntraAdapterConfigKeys.ClientSecret] =
            string.IsNullOrWhiteSpace(options.ClientSecret) ? null : string.Empty;
        Set(EntraAdapterConfigKeys.AppRoleResourceId, options.AppRoleResourceId);
        Set(EntraAdapterConfigKeys.GraphBaseUrl, options.GraphBaseUrl);
        Set(EntraAdapterConfigKeys.DefaultEmailDomain, options.DefaultEmailDomain);
    }

    /// <summary>True when a non-empty static value was captured for the key.</summary>
    public bool HasValue(string key)
        => _values.TryGetValue(key, out var v) && v is not null;

    /// <summary>
    /// The captured non-secret static value, or null for a secret key / absent
    /// value. For the secret key this is always null even when present.
    /// </summary>
    public string? GetValue(string key)
    {
        if (!_values.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }
        // Secrets are stored as empty-string sentinel (present, value withheld).
        return v.Length == 0 ? null : v;
    }

    private void Set(string key, string? value)
        => _values[key] = string.IsNullOrWhiteSpace(value) ? null : value;
}
