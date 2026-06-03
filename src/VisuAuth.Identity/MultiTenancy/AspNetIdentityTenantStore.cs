using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// <see cref="ITenantStore"/> implementation backed by the consumer's
/// <see cref="IVisuAuthMetadataDbContext"/> (typically the same DbContext
/// that hosts ASP.NET Identity tables). Reads / writes the
/// <c>VisuAuthTenants</c> table directly via EF Core.
/// </summary>
/// <typeparam name="TUser">The consumer's Identity user type. Used to count
/// members per tenant without going through any global query filter.</typeparam>
public sealed class AspNetIdentityTenantStore<TUser>(
    IVisuAuthMetadataDbContext metadata,
    UserManager<TUser> userManager,
    TimeProvider timeProvider) : ITenantStore
    where TUser : IdentityUser, IMultiTenantEntity
{
    private readonly IVisuAuthMetadataDbContext _metadata =
        metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly UserManager<TUser> _userManager =
        userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _metadata.VisuAuthTenants
            .AsNoTracking()
            .OrderBy(t => t.DisplayName)
            .ToListAsync(cancellationToken);

        // Member counts ignore the global tenant filter — we explicitly want
        // the count for OTHER tenants here, regardless of the current scope.
        var memberCounts = await _userManager.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId != null)
            .GroupBy(u => u.TenantId!)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var lookup = memberCounts.ToDictionary(x => x.TenantId, x => x.Count, StringComparer.Ordinal);

        return tenants.Select(t => new TenantSummary
        {
            Id = t.Id,
            DisplayName = string.IsNullOrWhiteSpace(t.DisplayName) ? t.Id : t.DisplayName,
            MemberCount = lookup.TryGetValue(t.Id, out var count) ? count : 0,
            CreatedAt = t.CreatedAt,
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<TenantSummary?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var tenant = await _metadata.VisuAuthTenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (tenant is null)
        {
            return null;
        }

        var memberCount = await _userManager.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == id, cancellationToken);

        return new TenantSummary
        {
            Id = tenant.Id,
            DisplayName = string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Id : tenant.DisplayName,
            MemberCount = memberCount,
            CreatedAt = tenant.CreatedAt,
        };
    }

    /// <inheritdoc />
    public async Task<StoreResult> CreateAsync(string id, string? displayName, CancellationToken cancellationToken = default)
    {
        var trimmedId = id?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedId))
        {
            return StoreResult.Failure("Tenant id is required.");
        }

        // The id doubles as a URL / header value — keep it boring.
        if (trimmedId.Length > 64 || trimmedId.Any(c => !IsAllowedIdChar(c)))
        {
            return StoreResult.Failure(
                "Tenant id must be 1-64 characters of letters, digits, '-', '_' or '.'.");
        }

        if (await _metadata.VisuAuthTenants.AnyAsync(t => t.Id == trimmedId, cancellationToken))
        {
            return StoreResult.Failure($"Tenant '{trimmedId}' already exists.");
        }

        var tenant = new VisuAuthTenant
        {
            Id = trimmedId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmedId : displayName!.Trim(),
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        _metadata.VisuAuthTenants.Add(tenant);
        await _metadata.SaveChangesAsync(cancellationToken);
        return StoreResult.Success(tenant.Id);
    }

    /// <inheritdoc />
    public async Task<StoreResult> RenameAsync(string id, string newDisplayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            return StoreResult.Failure("Display name is required.");
        }

        var tenant = await _metadata.VisuAuthTenants
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return StoreResult.Failure($"Tenant '{id}' was not found.");
        }

        tenant.DisplayName = newDisplayName.Trim();
        await _metadata.SaveChangesAsync(cancellationToken);
        return StoreResult.Success(tenant.Id);
    }

    /// <inheritdoc />
    public async Task<StoreResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var tenant = await _metadata.VisuAuthTenants
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return StoreResult.Failure($"Tenant '{id}' was not found.");
        }

        var memberCount = await _userManager.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == id, cancellationToken);
        if (memberCount > 0)
        {
            return StoreResult.Failure(
                $"Tenant '{id}' still has {memberCount} user(s). Reassign or delete them before removing the tenant.");
        }

        _metadata.VisuAuthTenants.Remove(tenant);
        await _metadata.SaveChangesAsync(cancellationToken);
        return StoreResult.Success(tenant.Id);
    }

    private static bool IsAllowedIdChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
}
