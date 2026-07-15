using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EntraCore.Stubs;

/// <summary>
/// Stub <see cref="IJwtIssuer"/> that the Entra adapter family registers
/// so the minimal-API <c>/visuauth/api/auth/login</c> endpoint (which
/// lists IJwtIssuer as a required dependency) can still be mapped at
/// startup even when no real issuer is wired.
/// </summary>
/// <remarks>
/// In Entra mode the JWT-issued mobile flow is satisfied by Microsoft's
/// own tokens (MSAL / OIDC against the tenant), so VisuAuth's HS256
/// issuer has nothing to do. <see cref="IssueAsync"/> always returns
/// <c>null</c> — the <c>AuthApi.IssueOrUnauthorized</c> branch treats
/// that as "User is no longer eligible to sign in" and surfaces a
/// clean 401.
/// </remarks>
public sealed class EntraNoOpJwtIssuer : IJwtIssuer
{
    public Task<JwtTokenResult?> IssueAsync(string userId, CancellationToken cancellationToken = default)
        => Task.FromResult<JwtTokenResult?>(null);

    public Task<JwtTokenResult?> ReissueAsync(
        string userId,
        string? presentedSecurityStamp,
        CancellationToken cancellationToken = default)
        => Task.FromResult<JwtTokenResult?>(null);
}
