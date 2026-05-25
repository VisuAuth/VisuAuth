using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Identity.MultiTenancy;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// <see cref="IExternalLoginFlow"/> implementation backed by ASP.NET Core
/// Identity. Wraps <see cref="SignInManager{TUser}"/>'s external-login surface
/// and applies the consumer-configured first-time strategy on accounts that
/// the provider has not yet linked to a local user.
/// </summary>
/// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
public sealed class AspNetIdentityExternalLoginFlow<TUser>(
    SignInManager<TUser> signInManager,
    UserManager<TUser> userManager,
    IExternalProviderConfigStore? configStore = null,
    IExternalProviderStaticConfigSnapshot? staticConfigSnapshot = null) : IExternalLoginFlow
    where TUser : IdentityUser
{
    private readonly SignInManager<TUser> _signInManager =
        signInManager ?? throw new ArgumentNullException(nameof(signInManager));
    private readonly UserManager<TUser> _userManager =
        userManager ?? throw new ArgumentNullException(nameof(userManager));
    /// <summary>
    /// Optional — when registered (via <c>AddVisuAuthExternalProviderConfigStore</c>),
    /// the provider list returned by <see cref="GetProvidersAsync"/> is
    /// filtered to admin-enabled rows. Without the store the flow falls
    /// back to "all registered schemes" — backward-compatible with
    /// consumers who never opt into the admin UI.
    /// </summary>
    private readonly IExternalProviderConfigStore? _configStore = configStore;
    /// <summary>
    /// Optional — exposes the credentials a scheme arrived with before our
    /// overlay (i.e. from appsettings / user-secrets / Program.cs lambdas).
    /// <see cref="GetProvidersAsync"/> consults this so a scheme stays
    /// visible on /visuauth/login when the secret lives in code and only
    /// the ClientId was edited via the admin UI — without it the button
    /// would silently disappear even though the login flow itself would
    /// have worked.
    /// </summary>
    private readonly IExternalProviderStaticConfigSnapshot? _staticConfigSnapshot = staticConfigSnapshot;

    /// <inheritdoc />
    public UserBackendCapabilities Capabilities { get; } = new()
    {
        SupportsLocalLogin = true,
        SupportsExternalProviders = true,
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalProviderInfo>> GetProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        var providers = schemes
            .Select(s => new ExternalProviderInfo
            {
                Scheme = s.Name,
                DisplayName = string.IsNullOrEmpty(s.DisplayName) ? s.Name : s.DisplayName,
            });

        if (_configStore is null)
        {
            return providers.ToArray();
        }

        // When the admin store IS wired, decide per scheme whether the
        // button should render. The rule mirrors what the OAuth handler
        // will actually see at request time, which is the UNION of DB
        // overlay and static (appsettings / user-secrets / Program.cs
        // lambda):
        //   * IsEnabled (DB row missing = treated as enabled, legacy)
        //   * AND ClientId present in DB OR static snapshot
        //   * AND ClientSecret present in DB OR static snapshot
        // Missing both ClientId and Secret → hidden, because clicking the
        // button would land the user on a 500 from the handler.
        var configs = await _configStore.ListAsync(tenantId: null, cancellationToken);
        var configsBySchema = configs.ToDictionary(c => c.Scheme, StringComparer.Ordinal);

        var live = providers.Where(p =>
        {
            configsBySchema.TryGetValue(p.Scheme, out var dbConfig);
            var staticView = _staticConfigSnapshot?.GetForScheme(p.Scheme);

            // No DB row at all → legacy enabled. Admin gets to opt out
            // explicitly by saving a row with IsEnabled=false.
            var isEnabled = dbConfig?.IsEnabled ?? true;
            if (!isEnabled)
            {
                return false;
            }

            var hasClientId = !string.IsNullOrWhiteSpace(dbConfig?.ClientId)
                              || staticView is { ClientId.Length: > 0 };
            var hasSecret = (dbConfig?.HasClientSecret == true)
                            || (staticView?.HasClientSecret == true);
            return hasClientId && hasSecret;
        });

        return live.ToArray();
    }

    /// <inheritdoc />
    public async Task<ExternalSignInResult> CompleteSignInAsync(
        ExternalLoginFirstTimeStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return ExternalSignInResult.NoExternalSession();
        }

        // bypassTwoFactor: true means external sign-in does not re-prompt
        // for TOTP. The provider already authenticated; layering 2FA here
        // would conflict with the provider's own MFA in most setups. A
        // future option could flip this for paranoid deployments.
        var attempt = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (attempt.Succeeded)
        {
            var existing = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            return ExternalSignInResult.Success(existing?.Id ?? string.Empty);
        }
        if (attempt.IsLockedOut)
        {
            return ExternalSignInResult.LockedOut();
        }
        if (attempt.IsNotAllowed)
        {
            return ExternalSignInResult.NotAllowed();
        }

        // ExternalLoginSignIn failed because no local user is linked yet.
        // Apply the consumer-configured first-time strategy.
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var name = info.Principal.FindFirstValue(ClaimTypes.Name)
                   ?? info.Principal.FindFirstValue(ClaimTypes.GivenName);

        return strategy switch
        {
            ExternalLoginFirstTimeStrategy.AutoCreate
                => await CreateAndLinkAsync(info, email, name),

            ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm
                => await LinkExistingOrConfirmAsync(info, email, name),

            ExternalLoginFirstTimeStrategy.AlwaysConfirm
                => ExternalSignInResult.RequiresConfirmation(info.LoginProvider, info.ProviderKey, email, name),

            _ => ExternalSignInResult.RequiresConfirmation(info.LoginProvider, info.ProviderKey, email, name),
        };
    }

    /// <inheritdoc />
    public async Task<ExternalPendingInfo?> GetPendingInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return null;
        }
        return new ExternalPendingInfo
        {
            Provider = info.LoginProvider,
            ProviderKey = info.ProviderKey,
            Email = info.Principal.FindFirstValue(ClaimTypes.Email),
            DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name)
                          ?? info.Principal.FindFirstValue(ClaimTypes.GivenName),
        };
    }

    /// <inheritdoc />
    public async Task<ExternalSignInResult> ConfirmAndCreateAsync(
        string email,
        string? userName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        cancellationToken.ThrowIfCancellationRequested();

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return ExternalSignInResult.NoExternalSession();
        }

        // If a user already owns this email, link the external login to
        // that account and sign in. Otherwise create a fresh user.
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return await LinkAndSignInAsync(existing, info);
        }

        return await CreateUserAndSignInAsync(info, email, userName, tenantId);
    }

    private async Task<ExternalSignInResult> CreateAndLinkAsync(
        ExternalLoginInfo info,
        string? email,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            // Provider returned no email — we can't auto-create without one,
            // so fall through to confirmation so the user supplies one.
            return ExternalSignInResult.RequiresConfirmation(info.LoginProvider, info.ProviderKey, email, name);
        }
        // Pass userName: null so CreateUserAndSignInAsync falls back to using
        // the email as the UserName. The provider's display name claim
        // ("Thiago Lugarini") almost always contains spaces and other
        // characters that Identity's default AllowedUserNameCharacters
        // rejects — using email is what every scaffolded external-login
        // template (Identity, Auth0, Clerk) does for the no-confirm path.
        // The display name stays available via UserManager.GetClaimsAsync if
        // a consumer wants it on the profile page.
        _ = name;
        return await CreateUserAndSignInAsync(info, email, userName: null, tenantId: null);
    }

    private async Task<ExternalSignInResult> LinkExistingOrConfirmAsync(
        ExternalLoginInfo info,
        string? email,
        string? name)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing is not null)
            {
                return await LinkAndSignInAsync(existing, info);
            }
        }
        return ExternalSignInResult.RequiresConfirmation(info.LoginProvider, info.ProviderKey, email, name);
    }

    private async Task<ExternalSignInResult> LinkAndSignInAsync(TUser user, ExternalLoginInfo info)
    {
        var addLogin = await _userManager.AddLoginAsync(user, info);
        if (!addLogin.Succeeded)
        {
            return ExternalSignInResult.Failed(addLogin.Errors.Select(e => e.Description).ToArray());
        }
        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        return ExternalSignInResult.Success(user.Id);
    }

    private async Task<ExternalSignInResult> CreateUserAndSignInAsync(
        ExternalLoginInfo info,
        string email,
        string? userName,
        string? tenantId)
    {
        // Activator.CreateInstance avoids requiring `new()` on TUser, which
        // would block abstract base classes (e.g. MultiTenantIdentityUser).
        var user = Activator.CreateInstance<TUser>();
        user.Email = email;
        user.UserName = string.IsNullOrWhiteSpace(userName) ? email : userName;
        // Provider already verified the email — mark confirmed so the
        // user does not get stuck behind a "please confirm your email" gate.
        user.EmailConfirmed = true;

        if (!string.IsNullOrWhiteSpace(tenantId)
            && user is IMultiTenantEntity multiTenant
            && string.IsNullOrEmpty(multiTenant.TenantId))
        {
            multiTenant.TenantId = tenantId.Trim();
        }

        var create = await _userManager.CreateAsync(user);
        if (!create.Succeeded)
        {
            return ExternalSignInResult.Failed(create.Errors.Select(e => e.Description).ToArray());
        }

        var addLogin = await _userManager.AddLoginAsync(user, info);
        if (!addLogin.Succeeded)
        {
            return ExternalSignInResult.Failed(addLogin.Errors.Select(e => e.Description).ToArray());
        }

        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        return ExternalSignInResult.Success(user.Id);
    }
}
