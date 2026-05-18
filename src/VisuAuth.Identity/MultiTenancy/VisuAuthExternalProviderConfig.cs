namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// VisuAuth-owned metadata row carrying admin-edited external-provider
/// credentials. Stored in the <c>VisuAuthExternalProviderConfigs</c> table
/// by <see cref="MultiTenantIdentityDbContext{TUser}"/>. The encrypted
/// ClientSecret is the ciphertext produced by ASP.NET Core's
/// <c>IDataProtectionProvider</c>; the store decrypts on demand for the
/// auth handler and never returns plaintext through the admin UI surface.
/// </summary>
/// <remarks>
/// CLAUDE.md §2.5 — VisuAuth-owned tables are explicit and documented.
/// Uninstalling VisuAuth never destroys consumer data; drop this table
/// manually if you want to roll back.
/// </remarks>
public sealed class VisuAuthExternalProviderConfig
{
    /// <summary>
    /// Synthetic primary key. A composite (Scheme, TenantId) key was the
    /// natural choice but EF Core refuses NULL key components — so we
    /// keep TenantId nullable (for the global-row case) and use a generated
    /// GUID for the PK, with (Scheme, TenantId) as a unique index instead.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Authentication scheme name (e.g. <c>"Microsoft"</c>, <c>"Google"</c>).
    /// Combined with <see cref="TenantId"/> forms a unique index so the same
    /// scheme can have a global row + per-tenant overrides.
    /// </summary>
    public string Scheme { get; set; } = string.Empty;

    /// <summary>
    /// Tenant id when this config belongs to a single tenant; <c>null</c>
    /// for global configs (the only mode supported in Phase 1). The column
    /// is here for forward compatibility with the per-tenant runtime in
    /// Phase 1.5.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>Human-readable display name shown on the login button.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Provider-issued public client id (safe to render).</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Ciphertext of the client secret. NEVER read directly — go through
    /// <c>EfCoreExternalProviderConfigStore.GetClientSecretAsync</c> which
    /// runs the IDataProtectionProvider unprotect step. Stored as a
    /// nullable string so a row without a secret yet (admin in mid-setup)
    /// remains distinguishable from a row with an empty secret.
    /// </summary>
    public string? EncryptedClientSecret { get; set; }

    /// <summary>Whether this provider should appear as a login button.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
