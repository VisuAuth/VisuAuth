namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// VisuAuth-owned metadata row carrying one admin-edited backend-adapter
/// setting (e.g. the Entra adapter's <c>TenantId</c> or <c>ClientSecret</c>).
/// Stored in the <c>VisuAuthAdapterConfigs</c> table by
/// <see cref="MultiTenantIdentityDbContext{TUser}"/>. A generic per-adapter
/// key/value bag: one row per (Adapter, Key).
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="IsSecret"/> is set, <see cref="Value"/> holds the ciphertext
/// produced by ASP.NET Core's <c>IDataProtectionProvider</c> — never read it
/// directly; go through <c>EfCoreAdapterConfigStore</c>, which decrypts on
/// demand server-side and never returns a secret's plaintext to the admin UI.
/// </para>
/// <para>
/// CLAUDE.md §2.5 — VisuAuth-owned tables are explicit and documented.
/// Uninstalling VisuAuth never destroys consumer data; drop this table
/// manually to roll back.
/// </para>
/// </remarks>
public sealed class VisuAuthAdapterConfig
{
    /// <summary>
    /// Synthetic primary key. (Adapter, Key) is the natural key but a generated
    /// GUID PK + unique index keeps the schema simple and consistent with the
    /// external-provider config table.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>Adapter the setting belongs to (e.g. <c>"Entra"</c>).</summary>
    public string Adapter { get; set; } = string.Empty;

    /// <summary>Setting name (e.g. <c>"TenantId"</c>, <c>"ClientSecret"</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The stored value. Plaintext for non-secret settings; DataProtection
    /// ciphertext when <see cref="IsSecret"/> is set.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Whether <see cref="Value"/> is an encrypted secret. Drives both
    /// decryption on read and "•••• stored" rendering in the admin UI.
    /// </summary>
    public bool IsSecret { get; set; }

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
