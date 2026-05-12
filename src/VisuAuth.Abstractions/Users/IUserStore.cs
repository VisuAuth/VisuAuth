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

    /// <summary>
    /// Enables or disables the user. When <paramref name="enabled"/> is <c>false</c>
    /// the user is locked out indefinitely (until an admin unlocks them).
    /// </summary>
    Task<UserResult> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the user's password to a freshly generated, policy-compliant temporary
    /// value that the admin hands back to the user.
    /// </summary>
    /// <remarks>
    /// On success the result's <see cref="UserResult.Metadata"/> contains
    /// <c>"temporaryPassword"</c> with the plaintext temporary password. The admin
    /// UI surfaces it once — VisuAuth does not persist the plaintext anywhere.
    /// </remarks>
    Task<UserResult> ResetPasswordAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables two-factor for the user and clears any authenticator keys, so the
    /// user can re-enrol from scratch.
    /// </summary>
    Task<UserResult> ResetTwoFactorAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces sign-out from every active session by rotating the user's security
    /// stamp. Existing cookies / refresh tokens become invalid on the next request
    /// that validates the stamp.
    /// </summary>
    Task<UserResult> RevokeSessionsAsync(string id, CancellationToken cancellationToken = default);
}
