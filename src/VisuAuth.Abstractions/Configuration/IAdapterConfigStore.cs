using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Configuration;

/// <summary>
/// Persistence contract for backend-adapter configuration managed through the
/// admin UI (e.g. the Microsoft Entra adapter's TenantId / ClientId /
/// ClientSecret). A generic per-adapter key/value bag: each row is one setting
/// for one adapter, with a flag marking whether the value is a secret.
/// </summary>
/// <remarks>
/// <para>
/// Encryption of secret values is the store's internal responsibility — the
/// contract never returns a secret's plaintext to UI callers.
/// <see cref="GetResolvedAsync"/> (which DOES decrypt) is for server-side
/// option-overlay configurators only; <see cref="ListAsync"/> (admin-facing)
/// returns <see cref="AdapterConfigEntryView.Value"/> for non-secret keys and
/// <see langword="null"/> for secrets, exposing only
/// <see cref="AdapterConfigEntryView.HasValue"/>.
/// </para>
/// <para>
/// The store records the override values an operator typed in the dashboard;
/// an adapter overlays them on top of the values bound from
/// <c>IConfiguration</c> / a configure lambda. A key absent from the store
/// means "no override — use the code/appsettings value".
/// </para>
/// </remarks>
public interface IAdapterConfigStore
{
    /// <summary>
    /// Returns the stored overrides for <paramref name="adapter"/> as a
    /// resolved key→value map with secret values decrypted. Server-side only
    /// (option-overlay configurators). Keys with no stored override are simply
    /// absent; an empty map means nothing is overridden.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetResolvedAsync(string adapter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the stored entries for <paramref name="adapter"/> for the admin
    /// UI. Secret entries never carry their plaintext — only
    /// <see cref="AdapterConfigEntryView.HasValue"/> is set.
    /// </summary>
    Task<IReadOnlyList<AdapterConfigEntryView>> ListAsync(string adapter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a batch of overrides in one unit of work. Per entry the
    /// <see cref="AdapterConfigValue.Value"/> is tri-state: <see langword="null"/>
    /// leaves the stored value untouched (preserve), <c>""</c> removes the
    /// override (fall back to code/appsettings), and any other string sets it
    /// (encrypted when <see cref="AdapterConfigValue.IsSecret"/>).
    /// </summary>
    Task<StoreResult> SaveAsync(SaveAdapterConfigCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Admin-facing read shape for one stored adapter setting. Carries the
/// non-secret value directly; for secrets <see cref="Value"/> is always
/// <see langword="null"/> and only <see cref="HasValue"/> indicates a stored
/// secret, so plaintext never reaches the browser.
/// </summary>
public sealed record AdapterConfigEntryView
{
    /// <summary>Storage key for the setting.</summary>
    public required string Key { get; init; }

    /// <summary>True when the value is a secret (its plaintext is never returned here).</summary>
    public bool IsSecret { get; init; }

    /// <summary>True when a value (secret or not) is stored for this key.</summary>
    public bool HasValue { get; init; }

    /// <summary>The stored value for non-secret keys; always <see langword="null"/> for secrets.</summary>
    public string? Value { get; init; }

    /// <summary>When this setting was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Write shape for a batch of adapter-config overrides.</summary>
public sealed record SaveAdapterConfigCommand
{
    /// <summary>Adapter key the settings belong to.</summary>
    public required string Adapter { get; init; }

    /// <summary>The per-key values to apply in this save.</summary>
    public required IReadOnlyList<AdapterConfigValue> Values { get; init; }
}

/// <summary>
/// One key's intended state in a save. <see cref="Value"/> is tri-state:
/// <see langword="null"/> = preserve, <c>""</c> = clear the override,
/// otherwise = set (encrypted when <see cref="IsSecret"/>).
/// </summary>
public sealed record AdapterConfigValue
{
    /// <summary>Storage key being set.</summary>
    public required string Key { get; init; }

    /// <summary>True when the value should be stored encrypted as a secret.</summary>
    public bool IsSecret { get; init; }

    /// <summary>Tri-state: <see langword="null"/> preserves the stored value, <c>""</c> clears the override, any other string sets it.</summary>
    public string? Value { get; init; }
}
