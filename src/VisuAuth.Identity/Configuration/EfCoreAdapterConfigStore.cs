using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Configuration;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Configuration;

/// <summary>
/// EF Core implementation of <see cref="IAdapterConfigStore"/>. Persists rows
/// in <see cref="VisuAuthAdapterConfig"/> through the consumer's metadata
/// DbContext, encrypting secret values at rest with ASP.NET Core's
/// <see cref="IDataProtectionProvider"/> (keys managed by the host — they
/// survive restarts when configured for persistent key storage).
/// </summary>
public sealed class EfCoreAdapterConfigStore(
    IVisuAuthMetadataDbContext db,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : IAdapterConfigStore
{
    /// <summary>
    /// DataProtection purpose — version-suffixed so a future rotation can
    /// decrypt legacy ciphertext via a parallel protector if needed.
    /// </summary>
    private const string ProtectorPurpose = "VisuAuth.AdapterConfig.Secret.v1";

    private readonly IVisuAuthMetadataDbContext _db =
        db ?? throw new ArgumentNullException(nameof(db));
    private readonly IDataProtector _protector = (dataProtectionProvider
        ?? throw new ArgumentNullException(nameof(dataProtectionProvider)))
        .CreateProtector(ProtectorPurpose);
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetResolvedAsync(
        string adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = await _db.VisuAuthAdapterConfigs
            .AsNoTracking()
            .Where(c => c.Adapter == adapter)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var value = Resolve(row);
            if (value is not null)
            {
                result[row.Key] = value;
            }
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdapterConfigEntryView>> ListAsync(
        string adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = await _db.VisuAuthAdapterConfigs
            .AsNoTracking()
            .Where(c => c.Adapter == adapter)
            .OrderBy(c => c.Key)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AdapterConfigEntryView
        {
            Key = r.Key,
            IsSecret = r.IsSecret,
            HasValue = !string.IsNullOrEmpty(r.Value),
            // Secret plaintext never leaves the store through the admin surface.
            Value = r.IsSecret ? null : r.Value,
            UpdatedAt = r.UpdatedAt,
        }).ToArray();
    }

    /// <inheritdoc />
    public async Task<StoreResult> SaveAsync(
        SaveAdapterConfigCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Adapter);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.Values.Count == 0)
        {
            return StoreResult.Success();
        }

        // Tracked load (not AsNoTracking) so updates / removes flush on save.
        var existing = await _db.VisuAuthAdapterConfigs
            .Where(c => c.Adapter == command.Adapter)
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(c => c.Key, StringComparer.Ordinal);
        var now = _timeProvider.GetUtcNow();

        // Collapse duplicate keys (last write wins) so a repeated key can't
        // Add a second row that violates the unique (Adapter, Key) index or
        // mutate an entity already marked for removal.
        var lastPerKey = new Dictionary<string, AdapterConfigValue>(StringComparer.Ordinal);
        foreach (var entry in command.Values.Where(v => !string.IsNullOrWhiteSpace(v.Key)))
        {
            lastPerKey[entry.Key] = entry;
        }

        foreach (var entry in lastPerKey.Values)
        {
            ApplyEntry(command.Adapter, entry, byKey, now);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return StoreResult.Success();
    }

    // Applies one entry's tri-state Value: null = preserve, "" = clear (remove
    // the row), else = set (encrypting first when the entry is a secret).
    private void ApplyEntry(
        string adapter,
        AdapterConfigValue entry,
        Dictionary<string, VisuAuthAdapterConfig> byKey,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null)
        {
            return;
        }

        byKey.TryGetValue(entry.Key, out var row);

        if (entry.Value.Length == 0)
        {
            if (row is not null)
            {
                _db.VisuAuthAdapterConfigs.Remove(row);
                byKey.Remove(entry.Key);
            }
            return;
        }

        var stored = entry.IsSecret ? _protector.Protect(entry.Value) : entry.Value;
        if (row is null)
        {
            var added = new VisuAuthAdapterConfig
            {
                Id = Guid.NewGuid(),
                Adapter = adapter,
                Key = entry.Key,
                Value = stored,
                IsSecret = entry.IsSecret,
                UpdatedAt = now,
            };
            _db.VisuAuthAdapterConfigs.Add(added);
            // Keep the in-batch view in sync with EF's tracked state.
            byKey[entry.Key] = added;
        }
        else
        {
            row.Value = stored;
            row.IsSecret = entry.IsSecret;
            row.UpdatedAt = now;
        }
    }

    private string? Resolve(VisuAuthAdapterConfig row)
    {
        if (string.IsNullOrEmpty(row.Value))
        {
            return null;
        }
        if (!row.IsSecret)
        {
            return row.Value;
        }
        try
        {
            return _protector.Unprotect(row.Value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Ciphertext produced under a different DataProtection key (e.g. an
            // ephemeral key lost on restart). Treat as "not configured" rather
            // than throwing through to the adapter.
            return null;
        }
    }
}
