using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;

namespace VisuAuth.EntraCore.Stubs;

/// <summary>
/// Fallback <see cref="IExternalLoginFlow"/> the Entra adapter family
/// registers so the Razor <c>LoginModel</c> (which lists
/// IExternalLoginFlow as a required constructor param) can be
/// activated even though the Entra family owns the external-provider
/// story differently than the Identity adapter.
/// </summary>
/// <remarks>
/// <para>
/// Each adapter passes the capability bag that matches its surface
/// (Workforce caps for VisuAuth.Entra, External caps for
/// VisuAuth.EntraExternal) — the LoginModel renders the
/// external-provider section based on
/// <see cref="UserBackendCapabilities.SupportsExternalProviders"/> too.
/// When that's false this flow's methods are reached only by callers
/// bypassing the UI; "failed" / "no session" / null is the honest
/// answer.
/// </para>
/// </remarks>
public sealed class EntraNoOpExternalLoginFlow(UserBackendCapabilities capabilities) : IExternalLoginFlow
{
    private static readonly ExternalProviderInfo[] EmptyProviders = [];

    public UserBackendCapabilities Capabilities { get; } =
        capabilities ?? throw new ArgumentNullException(nameof(capabilities));

    public Task<IReadOnlyList<ExternalProviderInfo>> GetProvidersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExternalProviderInfo>>(EmptyProviders);

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
