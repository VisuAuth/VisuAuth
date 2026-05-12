using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Users;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.Identity.Users;

/// <summary>
/// <see cref="IUserStore"/> implementation backed by ASP.NET Core Identity's
/// <see cref="UserManager{TUser}"/>. Read operations bypass the manager and
/// query <see cref="UserManager{TUser}.Users"/> directly for efficient
/// pagination and search.
/// </summary>
/// <typeparam name="TUser">The Identity user type (defaults to <see cref="IdentityUser"/>).</typeparam>
public sealed class AspNetIdentityUserStore<TUser>(
    UserManager<TUser> userManager,
    TimeProvider timeProvider) : IUserStore
    where TUser : IdentityUser
{
    private readonly UserManager<TUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public UserBackendCapabilities Capabilities { get; } = new()
    {
        SupportsLocalLogin = true,
        SupportsRegistration = true,
        SupportsPasswordReset = true,
        SupportsTwoFactorReset = true,
        SupportsImpersonation = true,
        SupportsCustomClaims = true,
        SupportsRoleManagement = true,
        SupportsAuditLog = false,        // Optional plugin
        SupportsBulkOperations = false,  // Coming in a follow-up PR
        SupportsSessionRevocation = true,
        SupportsExternalProviders = true,
        SupportsEmailConfirmation = true,
        SupportsLockout = true,
    };

    /// <inheritdoc />
    public async Task<UserSummary?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var user = await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null ? null : MapToSummary(user);
    }

    /// <inheritdoc />
    public async Task<UserDetail?> GetDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // FindByIdAsync (not the IQueryable path) is required here: claim/role/login
        // lookups below are wired to the tracked entity by Identity.
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var roles = await _userManager.GetRolesAsync(user);
        var claims = await _userManager.GetClaimsAsync(user);
        var logins = await _userManager.GetLoginsAsync(user);

        var now = _timeProvider.GetUtcNow();
        var isLockedOut = user.LockoutEnd is { } end && end > now;

        return new UserDetail
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            UserName = user.UserName,
            PhoneNumber = user.PhoneNumber,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            IsEnabled = !isLockedOut,
            TwoFactorEnabled = user.TwoFactorEnabled,
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd,
            AccessFailedCount = user.AccessFailedCount,
            // TenantId is projected when the consumer's TUser opts into
            // multi-tenancy. CreatedAt / LastSignInAt live on custom TUser
            // fields — specific adapters can override.
            TenantId = (user as IMultiTenantEntity)?.TenantId,
            CreatedAt = default,
            LastSignInAt = null,
            Roles = roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
            Claims = claims
                .Select(c => new UserClaim
                {
                    Type = c.Type,
                    Value = c.Value,
                    Issuer = string.IsNullOrEmpty(c.Issuer) ? null : c.Issuer,
                })
                .ToList(),
            ExternalLogins = logins
                .Select(l => new ExternalLogin
                {
                    Provider = l.LoginProvider,
                    ProviderKey = l.ProviderKey,
                    DisplayName = l.ProviderDisplayName,
                })
                .ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim().ToUpperInvariant();
            // NormalizedEmail / NormalizedUserName are populated by Identity on CreateAsync
            // and indexed — efficient search without pulling every row into memory.
            query = query.Where(u =>
                (u.NormalizedEmail != null && u.NormalizedEmail.Contains(term)) ||
                (u.NormalizedUserName != null && u.NormalizedUserName.Contains(term)));
        }

        if (filter.IsLockedOut is { } lockedOut)
        {
            var now = _timeProvider.GetUtcNow();
            query = lockedOut
                ? query.Where(u => u.LockoutEnd != null && u.LockoutEnd > now)
                : query.Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);
        }

        query = ApplyOrdering(query, filter);

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserSummary>
        {
            Items = users.Select(MapToSummary).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<UserResult> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return UserResult.Failure("Email is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Activate a fresh TUser via its parameterless constructor — every
        // IdentityUser subclass exposes one, and using `new TUser()` would
        // require a generic constraint we have not declared.
        var user = Activator.CreateInstance<TUser>();
        user.Email = command.Email.Trim();
        user.UserName = string.IsNullOrWhiteSpace(command.UserName)
            ? command.Email.Trim()
            : command.UserName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(command.PhoneNumber)
            ? null
            : command.PhoneNumber.Trim();
        user.EmailConfirmed = command.EmailConfirmed;

        // When no password is provided we generate a policy-compliant temporary
        // one and surface it back to the admin via Metadata, mirroring the
        // ResetPasswordAsync flow.
        var providedPassword = string.IsNullOrEmpty(command.Password) ? null : command.Password;
        var temporary = providedPassword is null
            ? TemporaryPasswordGenerator.Generate(_userManager.Options.Password)
            : null;
        var passwordToUse = providedPassword ?? temporary!;

        var result = await _userManager.CreateAsync(user, passwordToUse);
        if (!result.Succeeded)
        {
            return ToFailure(result, "Failed to create user.");
        }

        if (temporary is not null)
        {
            return UserResult.Success(user.Id, new Dictionary<string, string>
            {
                ["temporaryPassword"] = temporary,
            });
        }

        return UserResult.Success(user.Id);
    }

    /// <inheritdoc />
    public async Task<UserResult> UpdateAsync(string id, UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(command);

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserResult.Failure($"User '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();

        if (command.Email is not null && !string.Equals(command.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var result = await _userManager.SetEmailAsync(user, command.Email);
            CollectErrors(result, errors);
        }

        if (command.UserName is not null && !string.Equals(command.UserName, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var result = await _userManager.SetUserNameAsync(user, command.UserName);
            CollectErrors(result, errors);
        }

        if (command.PhoneNumber is not null && !string.Equals(command.PhoneNumber, user.PhoneNumber, StringComparison.Ordinal))
        {
            var result = await _userManager.SetPhoneNumberAsync(user, command.PhoneNumber);
            CollectErrors(result, errors);
        }

        return errors.Count == 0
            ? UserResult.Success(user.Id)
            : UserResult.Failure("Update failed.", errors);
    }

    /// <inheritdoc />
    public async Task<UserResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserResult.Failure($"User '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _userManager.DeleteAsync(user);
        return result.Succeeded
            ? UserResult.Success(user.Id)
            : ToFailure(result, "Failed to delete user.");
    }

    /// <inheritdoc />
    public async Task<UserResult> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserResult.Failure($"User '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Identity refuses to set a lockout end unless lockout is enabled for
        // this user. Flip the switch first so admins can lock a freshly seeded
        // account that has it off.
        if (!user.LockoutEnabled)
        {
            var enableLockout = await _userManager.SetLockoutEnabledAsync(user, true);
            if (!enableLockout.Succeeded)
            {
                return ToFailure(enableLockout, "Failed to enable lockout for user.");
            }
        }

        if (enabled)
        {
            var clear = await _userManager.SetLockoutEndDateAsync(user, lockoutEnd: null);
            if (!clear.Succeeded)
            {
                return ToFailure(clear, "Failed to unlock user.");
            }
            // Reset the failed-attempts counter so the user does not get
            // re-locked on the next misclick.
            await _userManager.ResetAccessFailedCountAsync(user);
            return UserResult.Success(user.Id);
        }

        // DateTimeOffset.MaxValue is the convention Identity itself uses
        // for "locked out forever".
        var lockResult = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        return lockResult.Succeeded
            ? UserResult.Success(user.Id)
            : ToFailure(lockResult, "Failed to lock user.");
    }

    /// <inheritdoc />
    public async Task<UserResult> ResetPasswordAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserResult.Failure($"User '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var temporary = TemporaryPasswordGenerator.Generate(_userManager.Options.Password);
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var apply = await _userManager.ResetPasswordAsync(user, token, temporary);

        if (!apply.Succeeded)
        {
            return ToFailure(apply, "Failed to reset password.");
        }

        // Rotate the security stamp so any active session is signed out — the
        // user must use the new temporary password to sign in again.
        await _userManager.UpdateSecurityStampAsync(user);

        return UserResult.Success(user.Id, new Dictionary<string, string>
        {
            ["temporaryPassword"] = temporary,
        });
    }

    /// <inheritdoc />
    public async Task<UserResult> ResetTwoFactorAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserResult.Failure($"User '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var disable = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disable.Succeeded)
        {
            return ToFailure(disable, "Failed to disable two-factor.");
        }

        // ResetAuthenticatorKeyAsync nukes the TOTP secret, so the user has to
        // enrol the authenticator from scratch on their next login.
        var resetKey = await _userManager.ResetAuthenticatorKeyAsync(user);
        if (!resetKey.Succeeded)
        {
            return ToFailure(resetKey, "Failed to reset authenticator key.");
        }

        return UserResult.Success(user.Id);
    }

    /// <inheritdoc />
    public async Task<UserResult> RevokeSessionsAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserResult.Failure($"User '{id}' was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stamp = await _userManager.UpdateSecurityStampAsync(user);
        return stamp.Succeeded
            ? UserResult.Success(user.Id)
            : ToFailure(stamp, "Failed to revoke sessions.");
    }

    private static void CollectErrors(IdentityResult result, List<string> errors)
    {
        if (result.Succeeded)
        {
            return;
        }
        foreach (var error in result.Errors)
        {
            errors.Add(error.Description);
        }
    }

    private static UserResult ToFailure(IdentityResult result, string fallback)
    {
        var messages = result.Errors.Select(e => e.Description).ToList();
        return UserResult.Failure(
            messages.Count == 0 ? fallback : messages[0],
            messages);
    }

    private static IQueryable<TUser> ApplyOrdering(IQueryable<TUser> query, UserFilter filter)
    {
        return filter.SortBy switch
        {
            UserSortBy.Email => filter.Descending
                ? query.OrderByDescending(u => u.Email)
                : query.OrderBy(u => u.Email),
            UserSortBy.UserName => filter.Descending
                ? query.OrderByDescending(u => u.UserName)
                : query.OrderBy(u => u.UserName),
            // CreatedAt and LastSignInAt live on custom TUser fields and cannot
            // be ordered generically. They fall back to ordering by Id, which is
            // a reasonable proxy for creation order with sequential GUIDs.
            UserSortBy.CreatedAt or UserSortBy.LastSignInAt => filter.Descending
                ? query.OrderByDescending(u => u.Id)
                : query.OrderBy(u => u.Id),
            _ => query.OrderBy(u => u.Id),
        };
    }

    private UserSummary MapToSummary(TUser user)
    {
        var now = _timeProvider.GetUtcNow();
        var isLockedOut = user.LockoutEnd is { } end && end > now;

        return new UserSummary
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            UserName = user.UserName,
            PhoneNumber = user.PhoneNumber,
            IsEnabled = !isLockedOut,
            EmailConfirmed = user.EmailConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            LockoutEnd = user.LockoutEnd,
            // TenantId is projected when the consumer's TUser opts into
            // multi-tenancy via IMultiTenantEntity. CreatedAt / LastSignInAt
            // live on custom TUser fields — specific adapters can override.
            TenantId = (user as IMultiTenantEntity)?.TenantId,
            CreatedAt = default,
            LastSignInAt = null,
        };
    }
}
