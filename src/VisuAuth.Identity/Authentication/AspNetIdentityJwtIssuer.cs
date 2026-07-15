using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// HS256 JWT issuer backed by ASP.NET Core Identity. Claims include
/// <c>sub</c>, <c>email</c>, <c>tenant_id</c> (when multi-tenancy is on),
/// <c>roles</c>, and a custom <c>visuauth_stamp</c> tracking the user's
/// security stamp so revocation flows on the admin side (lockout, "revoke
/// sessions") invalidate freshly-issued mobile tokens too.
/// </summary>
/// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
public sealed class AspNetIdentityJwtIssuer<TUser>(
    UserManager<TUser> userManager,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IJwtIssuer
    where TUser : IdentityUser
{
    /// <summary>Claim type the issuer stores the user's security stamp under.</summary>
    public const string SecurityStampClaimType = VisuAuthClaimTypes.SecurityStamp;

    private readonly UserManager<TUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly JwtOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public Task<JwtTokenResult?> IssueAsync(string userId, CancellationToken cancellationToken = default)
        => IssueCoreAsync(userId, presentedSecurityStamp: null, enforceStamp: false, cancellationToken);

    /// <inheritdoc />
    public Task<JwtTokenResult?> ReissueAsync(
        string userId,
        string? presentedSecurityStamp,
        CancellationToken cancellationToken = default)
        => IssueCoreAsync(userId, presentedSecurityStamp, enforceStamp: true, cancellationToken);

    private async Task<JwtTokenResult?> IssueCoreAsync(
        string userId,
        string? presentedSecurityStamp,
        bool enforceStamp,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        // Mirror the cookie-auth posture: a locked-out user cannot mint a fresh
        // token even with valid credentials.
        if (await _userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        // Refresh path: the presented token's stamp must still match the user's
        // current one, or revocation would not stick — a revoked token could be
        // exchanged here for a fresh, valid one. Fails closed: a token with no
        // stamp claim never matches.
        if (enforceStamp &&
            !string.Equals(presentedSecurityStamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            return null;
        }

        var key = ResolveSigningKey();
        var roles = await _userManager.GetRolesAsync(user);

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(SecurityStampClaimType, user.SecurityStamp ?? string.Empty),
        };

        var tenantId = (user as IMultiTenantEntity)?.TenantId;
        if (!string.IsNullOrEmpty(tenantId))
        {
            claims.Add(new Claim(VisuAuthClaimTypes.TenantId, tenantId));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new JwtTokenResult
        {
            AccessToken = accessToken,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            TenantId = tenantId,
        };
    }

    private SymmetricSecurityKey ResolveSigningKey()
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        // HS256 requires at least 256 bits (32 bytes). Surfacing this at issue
        // time gives a developer a far better error than the IdentityModel
        // exception buried in middleware.
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "VisuAuth JwtOptions.SigningKey must be at least 32 UTF-8 bytes for HS256. " +
                "Configure a long random secret in your secret store.");
        }
        return new SymmetricSecurityKey(keyBytes);
    }
}
