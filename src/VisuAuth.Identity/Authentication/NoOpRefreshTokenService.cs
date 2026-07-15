using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// Default <see cref="IRefreshTokenService"/> used until the consumer calls
/// <c>AddVisuAuthRefreshTokens()</c>. Reports <see cref="IsEnabled"/> =
/// <c>false</c> so the auth API keeps its legacy behaviour (reissuing from the
/// access token) without callers having to check configuration.
/// </summary>
public sealed class NoOpRefreshTokenService : IRefreshTokenService
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task<string?> IssueAsync(string userId, string? tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<RefreshRedemption> RedeemAsync(string presentedToken, CancellationToken cancellationToken = default)
        => Task.FromResult(RefreshRedemption.Failed);

    /// <inheritdoc />
    public Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
