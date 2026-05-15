using Microsoft.AspNetCore.Identity;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;

// Both Identity and our abstraction define a type called SignInResult; alias
// theirs to keep ours unqualified throughout the file.
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using SignInResult = VisuAuth.Abstractions.Authentication.SignInResult;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// <see cref="IAuthenticationFlow"/> implementation backed by ASP.NET Core
/// Identity. End-user pages and the mobile API both consume this so the
/// surface stays uniform across consumption channels.
/// </summary>
/// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
public sealed class AspNetIdentitySignInFlow<TUser>(
    SignInManager<TUser> signInManager,
    UserManager<TUser> userManager) : IAuthenticationFlow
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
        SupportsRegistration = true,
        SupportsPasswordReset = true,
        SupportsTwoFactor = true,
        SupportsTwoFactorReset = true,
        SupportsImpersonation = true,
        SupportsCustomClaims = true,
        SupportsRoleManagement = true,
        SupportsAuditLog = false,
        SupportsBulkOperations = false,
        SupportsSessionRevocation = true,
        SupportsExternalProviders = true,
        SupportsEmailConfirmation = true,
        SupportsLockout = true,
    };

    /// <inheritdoc />
    public async Task<SignInResult> SignInWithPasswordAsync(
        string emailOrUserName,
        string password,
        bool persistent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emailOrUserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        cancellationToken.ThrowIfCancellationRequested();

        // Look up by email first (the common case for end-user login),
        // fall back to username so admins can sign in with either.
        var user = await _userManager.FindByEmailAsync(emailOrUserName)
                   ?? await _userManager.FindByNameAsync(emailOrUserName);
        if (user is null)
        {
            return SignInResult.InvalidCredentials();
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            password,
            persistent,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return SignInResult.Success(user.Id);
        }
        if (result.RequiresTwoFactor)
        {
            return SignInResult.RequiresTwoFactor(user.Id);
        }
        if (result.IsLockedOut)
        {
            return SignInResult.LockedOut();
        }
        if (result.IsNotAllowed)
        {
            return SignInResult.NotAllowed("Sign-in is not allowed for this account.");
        }
        return SignInResult.InvalidCredentials();
    }

    /// <inheritdoc />
    public async Task<UserResult> RegisterAsync(
        string email,
        string password,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        cancellationToken.ThrowIfCancellationRequested();

        // Activator.CreateInstance avoids requiring `new()` on TUser, which
        // would block abstract base classes like MultiTenantIdentityUser
        // from being used as the user type in DI.
        var user = Activator.CreateInstance<TUser>();
        user.Email = email.Trim();
        user.UserName = email.Trim();

        // tenantId is captured via the SaveChangesInterceptor when multi-tenancy
        // is wired; setting it here explicitly is a no-op for IdentityUser
        // subclasses that do not implement IMultiTenantEntity.
        if (!string.IsNullOrWhiteSpace(tenantId) &&
            user is MultiTenancy.IMultiTenantEntity multiTenant &&
            string.IsNullOrEmpty(multiTenant.TenantId))
        {
            multiTenant.TenantId = tenantId.Trim();
        }

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return ToFailure(result, "Failed to register user.");
        }

        // Surface the email-confirmation token so the end-user UI can build
        // a clickable confirm link (development mode) or hand it off to an
        // email sender (production, when one is wired). The page is
        // responsible for not leaking the token to the wrong audience.
        var metadata = new Dictionary<string, string>();
        if (Capabilities.SupportsEmailConfirmation && !user.EmailConfirmed)
        {
            var confirmationToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            if (!string.IsNullOrEmpty(confirmationToken))
            {
                metadata["emailConfirmationToken"] = confirmationToken;
            }
        }
        return UserResult.Success(user.Id, metadata);
    }

    /// <inheritdoc />
    public async Task<UserResult> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Return success even when the user is unknown so attackers cannot
            // probe for valid emails. The end-user UI surfaces a generic
            // "if the email exists, a reset link was sent" message.
            return UserResult.Success();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        return UserResult.Success(user.Id, new Dictionary<string, string>
        {
            ["resetToken"] = token,
        });
    }

    /// <inheritdoc />
    public async Task<UserResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return UserResult.Failure("Invalid reset link.");
        }

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded
            ? UserResult.Success(user.Id)
            : ToFailure(result, "Failed to reset password.");
    }

    /// <inheritdoc />
    public async Task<UserResult> ConfirmEmailAsync(
        string userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserResult.Failure("Invalid confirmation link.");
        }

        var result = await _userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? UserResult.Success(user.Id)
            : ToFailure(result, "Failed to confirm email.");
    }

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _signInManager.SignOutAsync();
    }

    private static UserResult ToFailure(IdentityResult result, string fallback)
    {
        var messages = result.Errors.Select(e => e.Description).ToList();
        return UserResult.Failure(
            messages.Count == 0 ? fallback : messages[0],
            messages);
    }
}
