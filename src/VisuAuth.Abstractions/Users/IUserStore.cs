using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Users;

/// <summary>
/// The backend-agnostic user store consumed by the admin UI and the management API.
/// </summary>
/// <remarks>
/// Implementations should consult <see cref="Capabilities"/> first: operations that
/// the backend does not support should throw <see cref="NotSupportedException"/>
/// rather than silently succeed.
/// </remarks>
public interface IUserStore
{
    /// <summary>Features this backend supports. Inspected at runtime by the UI.</summary>
    UserBackendCapabilities Capabilities { get; }

    Task<UserSummary?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the full detail projection of a user (claims, roles, external logins, lockout
    /// and 2FA state). Returns <c>null</c> when no user matches <paramref name="id"/>.
    /// </summary>
    Task<UserDetail?> GetDetailAsync(string id, CancellationToken cancellationToken = default);

    Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken cancellationToken = default);

    Task<UserResult> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken = default);

    Task<UserResult> UpdateAsync(string id, UpdateUserCommand command, CancellationToken cancellationToken = default);

    Task<UserResult> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<UserResult> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default);

    Task<UserResult> ResetPasswordAsync(string id, CancellationToken cancellationToken = default);

    Task<UserResult> ResetTwoFactorAsync(string id, CancellationToken cancellationToken = default);

    Task<UserResult> RevokeSessionsAsync(string id, CancellationToken cancellationToken = default);
}
