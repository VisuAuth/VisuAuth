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
    UserManager<TUser> userManager) : IExternalLoginFlow
    where TUser : IdentityUser
{
    private readonly SignInManager<TUser> _signInManager =
        signInManager ?? throw new ArgumentNullException(nameof(signInManager));
    private readonly UserManager<TUser> _userManager =
        userManager ?? throw new ArgumentNullException(nameof(userManager));

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
        return schemes
            .Select(s => new ExternalProviderInfo
            {
                Scheme = s.Name,
                DisplayName = string.IsNullOrEmpty(s.DisplayName) ? s.Name : s.DisplayName,
            })
            .ToArray();
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
