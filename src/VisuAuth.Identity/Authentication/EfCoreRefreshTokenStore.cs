using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// EF-backed <see cref="IRefreshTokenService"/>: opaque, single-use, rotating
/// refresh tokens with replay detection. Registered by
/// <c>AddVisuAuthRefreshTokens()</c>.
/// </summary>
public sealed class EfCoreRefreshTokenStore(
    IVisuAuthMetadataDbContext db,
    IOptions<RefreshTokenOptions> options,
    TimeProvider timeProvider) : IRefreshTokenService
{
    /// <summary>256 bits of entropy — the token is the only secret here.</summary>
    private const int TokenBytes = 32;

    private readonly IVisuAuthMetadataDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly RefreshTokenOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public async Task<string?> IssueAsync(
        string userId,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var raw = CreateRawToken();
        var now = _timeProvider.GetUtcNow();

        _db.VisuAuthRefreshTokens.Add(new VisuAuthRefreshToken
        {
            TokenHash = Hash(raw),
            FamilyId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            TenantId = tenantId,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.Lifetime),
        });

        await _db.SaveChangesAsync(cancellationToken);
        return raw;
    }

    /// <inheritdoc />
    public async Task<RefreshRedemption> RedeemAsync(
        string presentedToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return RefreshRedemption.Failed;
        }

        var hash = Hash(presentedToken);

        // IgnoreQueryFilters: the token itself proves which tenant the caller
        // belongs to, and we record that tenant on the row. Scoping the lookup
        // to the ambient tenant would make redemption depend on a header the
        // client controls.
        var existing = await _db.VisuAuthRefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return RefreshRedemption.Failed;
        }

        var now = _timeProvider.GetUtcNow();

        if (existing.RevokedAt is not null)
        {
            // A retired token came back. Either it leaked and an attacker is
            // replaying it, or the legitimate client is. We cannot tell them
            // apart, so we burn the whole family and force a fresh sign-in.
            await RevokeFamilyAsync(existing.FamilyId, now, cancellationToken);
            return RefreshRedemption.Failed;
        }

        if (existing.ExpiresAt <= now)
        {
            return RefreshRedemption.Failed;
        }

        // Rotate: retire the presented token and issue its replacement into the
        // same family.
        var replacement = CreateRawToken();
        var replacementHash = Hash(replacement);

        existing.RevokedAt = now;
        existing.ReplacedByHash = replacementHash;

        _db.VisuAuthRefreshTokens.Add(new VisuAuthRefreshToken
        {
            TokenHash = replacementHash,
            FamilyId = existing.FamilyId,
            UserId = existing.UserId,
            TenantId = existing.TenantId,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.Lifetime),
        });

        await _db.SaveChangesAsync(cancellationToken);

        return RefreshRedemption.Success(existing.UserId, existing.TenantId, replacement);
    }

    /// <inheritdoc />
    public async Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var now = _timeProvider.GetUtcNow();
        var live = await _db.VisuAuthRefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in live)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeFamilyAsync(string familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await _db.VisuAuthRefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string CreateRawToken()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    private static string Hash(string raw)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    /// <summary>URL-safe, unpadded — the token travels in JSON and URLs.</summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
