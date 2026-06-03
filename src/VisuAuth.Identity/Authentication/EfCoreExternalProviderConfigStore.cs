using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Common;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// EF Core implementation of <see cref="IExternalProviderConfigStore"/>.
/// Stores rows in <see cref="VisuAuthExternalProviderConfig"/> via the
/// consumer's metadata DbContext, with the client secret encrypted at rest
/// through ASP.NET Core's <see cref="IDataProtectionProvider"/> (keys
/// managed by the host — survive restarts when configured for persistent
/// key storage).
/// </summary>
public sealed class EfCoreExternalProviderConfigStore(
    IVisuAuthMetadataDbContext db,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : IExternalProviderConfigStore
{
    /// <summary>
    /// DataProtection purpose string — version-suffixed so a future rotation
    /// can decrypt legacy ciphertext via a parallel protector if needed.
    /// </summary>
    private const string ProtectorPurpose = "VisuAuth.ExternalProvider.ClientSecret.v1";

    private readonly IVisuAuthMetadataDbContext _db =
        db ?? throw new ArgumentNullException(nameof(db));
    private readonly IDataProtector _protector = (dataProtectionProvider
        ?? throw new ArgumentNullException(nameof(dataProtectionProvider)))
        .CreateProtector(ProtectorPurpose);
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalProviderConfigView>> ListAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 1: global-only. Per-tenant filtering arrives in Phase 1.5
        // — the column is here so the schema is forward-compatible.
        var rows = await _db.VisuAuthExternalProviderConfigs
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Scheme)
            .ToListAsync(cancellationToken);

        return rows.Select(ToView).ToArray();
    }

    /// <inheritdoc />
    public async Task<ExternalProviderConfigView?> GetAsync(
        string scheme,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        cancellationToken.ThrowIfCancellationRequested();

        var row = await _db.VisuAuthExternalProviderConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Scheme == scheme && c.TenantId == tenantId, cancellationToken);
        return row is null ? null : ToView(row);
    }

    /// <inheritdoc />
    public async Task<string?> GetClientSecretAsync(
        string scheme,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        cancellationToken.ThrowIfCancellationRequested();

        var row = await _db.VisuAuthExternalProviderConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Scheme == scheme && c.TenantId == tenantId, cancellationToken);
        if (row is null || string.IsNullOrEmpty(row.EncryptedClientSecret))
        {
            return null;
        }
        try
        {
            return _protector.Unprotect(row.EncryptedClientSecret);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Ciphertext was produced under a different DataProtection key
            // (e.g. ephemeral key from a previous run lost on restart).
            // Surface as null so callers treat the provider as misconfigured
            // instead of throwing through to the auth pipeline.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<StoreResult> SaveAsync(
        SaveExternalProviderConfigCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DisplayName);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await _db.VisuAuthExternalProviderConfigs
            .FirstOrDefaultAsync(
                c => c.Scheme == command.Scheme && c.TenantId == command.TenantId,
                cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var encryptedSecret = ResolveEncryptedSecret(command.PlainTextClientSecret, existing?.EncryptedClientSecret);

        if (existing is null)
        {
            _db.VisuAuthExternalProviderConfigs.Add(new VisuAuthExternalProviderConfig
            {
                Id = Guid.NewGuid(),
                Scheme = command.Scheme,
                TenantId = command.TenantId,
                DisplayName = command.DisplayName,
                ClientId = NormaliseOrNull(command.ClientId),
                EncryptedClientSecret = encryptedSecret,
                IsEnabled = command.IsEnabled,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.DisplayName = command.DisplayName;
            existing.ClientId = NormaliseOrNull(command.ClientId);
            existing.EncryptedClientSecret = encryptedSecret;
            existing.IsEnabled = command.IsEnabled;
            existing.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return StoreResult.Success();
    }

    /// <inheritdoc />
    public async Task<StoreResult> SetEnabledAsync(
        string scheme,
        string? tenantId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        cancellationToken.ThrowIfCancellationRequested();

        var row = await _db.VisuAuthExternalProviderConfigs
            .FirstOrDefaultAsync(c => c.Scheme == scheme && c.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            return StoreResult.Failure($"No external provider config for scheme '{scheme}'.");
        }
        row.IsEnabled = isEnabled;
        row.UpdatedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return StoreResult.Success();
    }

    /// <inheritdoc />
    public async Task<StoreResult> EnsureSchemeAsync(
        string scheme,
        string displayName,
        string? tenantId = null,
        string? defaultClientId = null,
        bool defaultIsEnabled = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        cancellationToken.ThrowIfCancellationRequested();

        var exists = await _db.VisuAuthExternalProviderConfigs
            .AsNoTracking()
            .AnyAsync(c => c.Scheme == scheme && c.TenantId == tenantId, cancellationToken);
        if (exists)
        {
            return StoreResult.Success();
        }

        _db.VisuAuthExternalProviderConfigs.Add(new VisuAuthExternalProviderConfig
        {
            Id = Guid.NewGuid(),
            Scheme = scheme,
            TenantId = tenantId,
            DisplayName = displayName,
            ClientId = NormaliseOrNull(defaultClientId),
            EncryptedClientSecret = null,
            IsEnabled = defaultIsEnabled,
            UpdatedAt = _timeProvider.GetUtcNow(),
        });
        await _db.SaveChangesAsync(cancellationToken);
        return StoreResult.Success();
    }

    /// <inheritdoc />
    public async Task<StoreResult> DeleteAsync(
        string scheme,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        cancellationToken.ThrowIfCancellationRequested();

        var row = await _db.VisuAuthExternalProviderConfigs
            .FirstOrDefaultAsync(c => c.Scheme == scheme && c.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            // Idempotent: no row = nothing to delete. The caller treats this
            // as success because the post-condition (no row for that scheme)
            // already holds.
            return StoreResult.Success();
        }

        _db.VisuAuthExternalProviderConfigs.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return StoreResult.Success();
    }

    /// <summary>
    /// Encryption rule used by Save:
    /// — caller passes a new plaintext  → encrypt and replace.
    /// — caller passes empty plaintext  → drop the secret entirely (used
    ///   when an admin wants to "clear" credentials without deleting the row).
    /// — caller passes null plaintext   → preserve whatever ciphertext was
    ///   already stored (used when admin edits only ClientId / IsEnabled).
    /// </summary>
    private string? ResolveEncryptedSecret(string? plainText, string? existingCiphertext)
    {
        if (plainText is null)
        {
            return existingCiphertext;
        }
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }
        return _protector.Protect(plainText);
    }

    private static string? NormaliseOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExternalProviderConfigView ToView(VisuAuthExternalProviderConfig row) => new()
    {
        Scheme = row.Scheme,
        TenantId = row.TenantId,
        DisplayName = row.DisplayName,
        ClientId = row.ClientId,
        HasClientSecret = !string.IsNullOrEmpty(row.EncryptedClientSecret),
        IsEnabled = row.IsEnabled,
        UpdatedAt = row.UpdatedAt,
    };
}
