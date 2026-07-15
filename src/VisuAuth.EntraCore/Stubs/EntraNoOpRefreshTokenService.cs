using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EntraCore.Stubs;

/// <summary>
/// Stub <see cref="IRefreshTokenService"/> the Entra adapter family registers
/// so the auth API resolves cleanly in Entra-only deployments.
/// </summary>
/// <remarks>
/// Microsoft issues and refreshes its own tokens in Entra mode, so VisuAuth's
/// refresh-token store has nothing to do. Reporting
/// <see cref="IsEnabled"/> = <c>false</c> keeps the endpoints on their
/// Entra-appropriate path rather than advertising a refresh token that would
/// never work.
/// </remarks>
public sealed class EntraNoOpRefreshTokenService : IRefreshTokenService
{
    public bool IsEnabled => false;

    public Task<string?> IssueAsync(string userId, string? tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<RefreshRedemption> RedeemAsync(string presentedToken, CancellationToken cancellationToken = default)
        => Task.FromResult(RefreshRedemption.Failed);

    public Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
