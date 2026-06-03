using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Persistence contract for external provider credentials managed through
/// the admin UI. Encryption of <c>ClientSecret</c> is the store's internal
/// responsibility — the contract never returns plaintext to UI callers
/// (use <see cref="GetClientSecretAsync"/> server-side from the auth
/// options configurator only).
/// </summary>
/// <remarks>
/// VisuAuth ships only pre-registered provider TYPES (Microsoft, Google,
/// Apple, GitHub in the sample). The store records the per-scheme
/// credentials + enabled flag; runtime registration of new TYPES still
/// requires code changes in <c>Program.cs</c>.
/// </remarks>
public interface IExternalProviderConfigStore
{
    /// <summary>
    /// Lists all configs visible in the given tenant scope. Pass
    /// <paramref name="tenantId"/> = <c>null</c> for global configs only;
    /// per-tenant configs are out of scope in Phase 1 (the column exists
    /// on the entity for forward-compat but the store always operates on
    /// global rows for now).
    /// </summary>
    Task<IReadOnlyList<ExternalProviderConfigView>> ListAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Returns a single config, or null when absent.</summary>
    Task<ExternalProviderConfigView?> GetAsync(string scheme, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the decrypted client secret in plaintext — for server-side
    /// auth handler use ONLY. Never call from UI / admin pages.
    /// </summary>
    Task<string?> GetClientSecretAsync(string scheme, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a config. When <see cref="SaveExternalProviderConfigCommand.PlainTextClientSecret"/>
    /// is null on an UPDATE, the existing secret is preserved (lets the UI
    /// edit ClientId / IsEnabled without re-typing the secret).
    /// </summary>
    Task<StoreResult> SaveAsync(SaveExternalProviderConfigCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flips a single config's enabled flag — used by the admin's bulk
    /// enable/disable surface and individual row toggles.
    /// </summary>
    Task<StoreResult> SetEnabledAsync(string scheme, string? tenantId, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently creates a row for <paramref name="scheme"/> if one does
    /// not exist yet. Used by the host's startup-time seed step to surface
    /// every scheme the consumer registered with <c>AddXxx</c> in the admin
    /// list — empty configs would otherwise stay invisible. Does nothing
    /// when a row already exists (admin's edits never get clobbered by a
    /// restart).
    /// </summary>
    /// <param name="defaultClientId">Optional ClientId to seed from
    /// appsettings / env / user-secrets so a working appsettings-based
    /// setup keeps rendering its button immediately after the store goes
    /// live.</param>
    /// <param name="defaultIsEnabled">Whether the seeded row starts in
    /// the enabled state. Pass <c>true</c> when <paramref name="defaultClientId"/>
    /// is present AND a ClientSecret is also configured at the static-options
    /// layer; <c>false</c> otherwise so the admin opts-in explicitly.</param>
    Task<StoreResult> EnsureSchemeAsync(
        string scheme,
        string displayName,
        string? tenantId = null,
        string? defaultClientId = null,
        bool defaultIsEnabled = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the row for <paramref name="scheme"/> entirely. Used by the
    /// admin when a row is orphaned (DB has credentials for a scheme the
    /// host no longer wired through <c>AddVisuAuthDynamicExternalProviderOptions</c>),
    /// so stale credentials don't linger after a provider type is dropped
    /// from <c>Program.cs</c>. No-op when no matching row exists.
    /// </summary>
    Task<StoreResult> DeleteAsync(string scheme, string? tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-shape returned to the admin UI. Crucially carries
/// <see cref="HasClientSecret"/> as a boolean rather than the secret itself
/// so the admin page can render "•••• stored — click Replace to change"
/// without ever flushing the plaintext to the browser.
/// </summary>
public sealed record ExternalProviderConfigView
{
    public required string Scheme { get; init; }
    public required string DisplayName { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public bool HasClientSecret { get; init; }
    public bool IsEnabled { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Write-shape from the admin save form. <c>PlainTextClientSecret</c> is
/// null when the admin edits without retyping the secret — the store
/// preserves the existing encrypted value in that case.
/// </summary>
public sealed record SaveExternalProviderConfigCommand
{
    public required string Scheme { get; init; }
    public required string DisplayName { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public string? PlainTextClientSecret { get; init; }
    public bool IsEnabled { get; init; }
}
