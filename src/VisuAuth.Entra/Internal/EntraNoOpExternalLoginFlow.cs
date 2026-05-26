using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.Entra.Internal;

/// <summary>
/// Fallback <see cref="IExternalLoginFlow"/> the Entra adapter registers so
/// the Razor <c>LoginModel</c> (which lists IExternalLoginFlow as a required
/// constructor param) can be activated even though Entra itself owns the
/// external-provider story.
/// </summary>
/// <remarks>
/// <para>
/// In Entra mode the "Sign in with Microsoft" button is what users see — but
/// it's the consequence of <see cref="EntraCapabilities.Value"/> declaring
/// <c>SupportsLocalLogin = false</c>, not anything this flow does. The
/// LoginModel renders the external-provider section based on
/// <c>Capabilities.SupportsExternalProviders</c> too — which the Entra
/// caps set to <c>false</c> because there are no third-party OAuth providers
/// to list (Entra IS the IdP). So the only path this flow's methods would
/// be reached on is a caller bypassing the UI; in that case "failed" /
/// "no session" / null is the honest answer.
/// </para>
/// </remarks>
internal sealed class EntraNoOpExternalLoginFlow : IExternalLoginFlow
{
    public UserBackendCapabilities Capabilities => EntraCapabilities.Value;

    public Task<IReadOnlyList<ExternalProviderInfo>> GetProvidersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExternalProviderInfo>>(Array.Empty<ExternalProviderInfo>());

    public Task<ExternalSignInResult> CompleteSignInAsync(
        ExternalLoginFirstTimeStrategy strategy,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ExternalSignInResult.NoExternalSession());

    public Task<ExternalSignInResult> ConfirmAndCreateAsync(
        string email,
        string? userName,
        string? tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ExternalSignInResult.Failed(["External provider flow is owned by Microsoft Entra; this stub is unreachable from the UI."]));

    public Task<ExternalPendingInfo?> GetPendingInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ExternalPendingInfo?>(null);
}
