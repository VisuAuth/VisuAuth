using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Backend-agnostic surface for external (OAuth / OIDC) sign-in providers.
/// Separate from <see cref="IAuthenticationFlow"/> so adapters that do not
/// own the provider story (and therefore set
/// <see cref="UserBackendCapabilities.SupportsExternalProviders"/> to
/// <c>false</c>) skip implementing it without affecting the password flow.
/// </summary>
public interface IExternalLoginFlow
{
    /// <summary>Mirrors the capability bag the rest of VisuAuth consults.</summary>
    UserBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Returns the providers currently registered with the host's
    /// authentication pipeline (e.g. Google, Microsoft, Apple) — the UI
    /// renders one button per item.
    /// </summary>
    Task<IReadOnlyList<ExternalProviderInfo>> GetProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a sign-in initiated by an external provider redirect. The
    /// caller (the OAuth callback page) is responsible for ensuring the
    /// host's external cookie is in scope before invoking this.
    /// </summary>
    /// <param name="strategy">
    /// First-time strategy applied when the external login is not yet
    /// linked to a local account.
    /// </param>
    Task<ExternalSignInResult> CompleteSignInAsync(
        ExternalLoginFirstTimeStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the new-account form rendered by
    /// <see cref="ExternalSignInOutcome.RequiresConfirmation"/>: creates a
    /// local user from the pending external claims (or links to an existing
    /// one when the email already exists), then signs the user in.
    /// </summary>
    Task<ExternalSignInResult> ConfirmAndCreateAsync(
        string email,
        string? userName,
        string? tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the in-scope external cookie and returns the provider's pending
    /// claims (provider name, email, display name) — used by the confirm page
    /// to pre-fill its form without relying on cross-request TempData.
    /// Returns <c>null</c> when no external cookie is present.
    /// </summary>
    Task<ExternalPendingInfo?> GetPendingInfoAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshot of the provider claims attached to the in-flight external cookie.
/// </summary>
public sealed record ExternalPendingInfo
{
    public required string Provider { get; init; }
    public required string ProviderKey { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// Read-only snapshot of an external provider that has been registered with
/// the host. Mirrors what <c>SignInManager.GetExternalAuthenticationSchemesAsync</c>
/// returns, kept VisuAuth-shaped so non-Identity adapters can produce it too.
/// </summary>
public sealed record ExternalProviderInfo
{
    /// <summary>Stable scheme name used by the auth handler (e.g. <c>"Microsoft"</c>).</summary>
    public required string Scheme { get; init; }

    /// <summary>Human-readable display name shown on the button (e.g. <c>"Microsoft"</c>).</summary>
    public required string DisplayName { get; init; }
}

/// <summary>
/// Determines what happens the first time a user signs in with an external
/// provider that is not yet linked to any local account.
/// </summary>
public enum ExternalLoginFirstTimeStrategy
{
    /// <summary>
    /// Creates a local user from the provider's claims (email + display
    /// name) without prompting. Email is marked confirmed because the
    /// provider already validated it. Lowest friction; matches the default
    /// behaviour of Auth0 / Clerk / Stytch.
    /// </summary>
    AutoCreate,

    /// <summary>
    /// Looks up an existing local user with the provider's email address.
    /// If found, links the external login automatically and signs in. If
    /// not, falls through to the confirmation page so the user can edit
    /// the email before account creation.
    /// </summary>
    AutoLinkByEmailOrConfirm,

    /// <summary>
    /// Always renders the confirmation page on first sign-in, regardless
    /// of whether the email matches an existing account. Highest friction,
    /// most explicit consent — useful when account-creation needs human
    /// input (terms acceptance, custom fields, etc.).
    /// </summary>
    AlwaysConfirm,
}

/// <summary>
/// Outcome of an external sign-in attempt.
/// </summary>
public enum ExternalSignInOutcome
{
    /// <summary>The user is fully signed in; the page may redirect to <c>returnUrl</c>.</summary>
    Success,

    /// <summary>
    /// The external login is not yet linked to a local account and the
    /// configured strategy demands a confirmation step.
    /// <see cref="ExternalSignInResult.PendingProvider"/>,
    /// <see cref="ExternalSignInResult.PendingProviderKey"/> and
    /// <see cref="ExternalSignInResult.PendingEmail"/> carry the form
    /// pre-fill values.
    /// </summary>
    RequiresConfirmation,

    /// <summary>The external cookie was missing or expired.</summary>
    NoExternalSession,

    /// <summary>The local user the external login is linked to is locked out.</summary>
    LockedOut,

    /// <summary>The local user is barred from signing in (e.g. requires email confirmation).</summary>
    NotAllowed,

    /// <summary>Account creation / link failed; <see cref="ExternalSignInResult.Errors"/> explains.</summary>
    Failed,
}

/// <summary>
/// Result of an external sign-in attempt.
/// </summary>
public sealed record ExternalSignInResult
{
    public required ExternalSignInOutcome Outcome { get; init; }

    /// <summary>Identifier of the signed-in local user (set on <see cref="ExternalSignInOutcome.Success"/>).</summary>
    public string? UserId { get; init; }

    /// <summary>Provider scheme name when the call needs follow-up confirmation.</summary>
    public string? PendingProvider { get; init; }

    /// <summary>Provider-issued user key when the call needs follow-up confirmation.</summary>
    public string? PendingProviderKey { get; init; }

    /// <summary>Email claim from the provider, when present.</summary>
    public string? PendingEmail { get; init; }

    /// <summary>Display name claim from the provider, when present.</summary>
    public string? PendingDisplayName { get; init; }

    /// <summary>Validation / Identity errors when <see cref="Outcome"/> is <see cref="ExternalSignInOutcome.Failed"/>.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ExternalSignInResult Success(string userId) => new()
    {
        Outcome = ExternalSignInOutcome.Success,
        UserId = userId,
    };

    public static ExternalSignInResult RequiresConfirmation(
        string provider,
        string providerKey,
        string? email,
        string? displayName) => new()
    {
        Outcome = ExternalSignInOutcome.RequiresConfirmation,
        PendingProvider = provider,
        PendingProviderKey = providerKey,
        PendingEmail = email,
        PendingDisplayName = displayName,
    };

    public static ExternalSignInResult NoExternalSession() => new()
    {
        Outcome = ExternalSignInOutcome.NoExternalSession,
    };

    public static ExternalSignInResult LockedOut() => new()
    {
        Outcome = ExternalSignInOutcome.LockedOut,
    };

    public static ExternalSignInResult NotAllowed() => new()
    {
        Outcome = ExternalSignInOutcome.NotAllowed,
    };

    public static ExternalSignInResult Failed(IReadOnlyList<string> errors) => new()
    {
        Outcome = ExternalSignInOutcome.Failed,
        Errors = errors,
    };
}

/// <summary>
/// Consumer-tunable options for the external-login flow.
/// </summary>
public sealed class ExternalLoginOptions
{
    /// <summary>
    /// Strategy applied when the provider's external login has no matching
    /// local account yet. Defaults to
    /// <see cref="ExternalLoginFirstTimeStrategy.AutoCreate"/> — frictionless
    /// out of the box, swap to <see cref="ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm"/>
    /// when account-create needs human input.
    /// </summary>
    public ExternalLoginFirstTimeStrategy FirstTimeStrategy { get; set; } = ExternalLoginFirstTimeStrategy.AutoCreate;

    /// <summary>
    /// When true (default) the auto-created user has <c>EmailConfirmed = true</c>
    /// — the provider already verified the email. Set false for paranoid setups
    /// that want a separate VisuAuth-issued confirmation step on top.
    /// </summary>
    public bool TrustProviderEmailConfirmation { get; set; } = true;
}
