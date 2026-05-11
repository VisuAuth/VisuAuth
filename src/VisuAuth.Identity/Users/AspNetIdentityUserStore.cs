using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Users;

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
    public Task<UserResult> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("CreateAsync ships in a follow-up PR.");

    /// <inheritdoc />
    public Task<UserResult> UpdateAsync(string id, UpdateUserCommand command, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("UpdateAsync ships in a follow-up PR.");

    /// <inheritdoc />
    public Task<UserResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("DeleteAsync ships in a follow-up PR.");

    /// <inheritdoc />
    public Task<UserResult> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("SetEnabledAsync ships in a follow-up PR.");

    /// <inheritdoc />
    public Task<UserResult> ResetPasswordAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ResetPasswordAsync ships in a follow-up PR.");

    /// <inheritdoc />
    public Task<UserResult> ResetTwoFactorAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ResetTwoFactorAsync ships in a follow-up PR.");

    /// <inheritdoc />
    public Task<UserResult> RevokeSessionsAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("RevokeSessionsAsync ships in a follow-up PR.");

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
            // TenantId / CreatedAt / LastSignInAt depend on the consumer's custom
            // TUser — specific adapters can override this mapping.
            TenantId = null,
            CreatedAt = default,
            LastSignInAt = null,
        };
    }
}
