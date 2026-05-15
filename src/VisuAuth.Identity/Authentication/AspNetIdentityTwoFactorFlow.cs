using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using SignInResult = VisuAuth.Abstractions.Authentication.SignInResult;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// <see cref="ITwoFactorFlow"/> implementation backed by ASP.NET Core Identity's
/// authenticator (TOTP) APIs. Drives the <c>/visuauth/two-factor/*</c> pages.
/// </summary>
/// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
public sealed class AspNetIdentityTwoFactorFlow<TUser>(
    UserManager<TUser> userManager,
    SignInManager<TUser> signInManager,
    IOptions<TwoFactorIssuerOptions> issuerOptions) : ITwoFactorFlow
    where TUser : IdentityUser
{
    /// <summary>Number of recovery codes generated per <see cref="GenerateRecoveryCodesAsync"/> call.</summary>
    public const int RecoveryCodeCountDefault = 10;

    private const string UserNotFoundError = "User not found.";

    private readonly UserManager<TUser> _userManager =
        userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly SignInManager<TUser> _signInManager =
        signInManager ?? throw new ArgumentNullException(nameof(signInManager));
    private readonly TwoFactorIssuerOptions _issuerOptions =
        issuerOptions?.Value ?? throw new ArgumentNullException(nameof(issuerOptions));

    /// <inheritdoc />
    public UserBackendCapabilities Capabilities { get; } = new()
    {
        SupportsLocalLogin = true,
        SupportsTwoFactor = true,
        SupportsTwoFactorReset = true,
    };

    /// <inheritdoc />
    public async Task<AuthenticatorSetup?> GetAuthenticatorSetupAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        // GetAuthenticatorKeyAsync returns null until the user has one — call
        // ResetAuthenticatorKeyAsync on first visit so the QR is always live.
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var account = user.Email ?? user.UserName ?? user.Id;
        return new AuthenticatorSetup
        {
            UserId = user.Id,
            AccountName = account,
            FormattedKey = OtpAuthUriBuilder.FormatForManualEntry(key),
            RawKey = key,
            OtpAuthUri = OtpAuthUriBuilder.Build(_issuerOptions.Issuer, account, key),
        };
    }

    /// <inheritdoc />
    public async Task<UserResult> ResetAuthenticatorKeyAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserResult.Failure(UserNotFoundError);
        }

        // Disable 2FA before rotating the key so a stale code from the old
        // authenticator can never satisfy the new one.
        await _userManager.SetTwoFactorEnabledAsync(user, false);
        var reset = await _userManager.ResetAuthenticatorKeyAsync(user);
        return reset.Succeeded ? UserResult.Success(user.Id) : ToFailure(reset, "Failed to reset authenticator key.");
    }

    /// <inheritdoc />
    public async Task<UserResult> EnableTwoFactorAsync(
        string userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = OtpAuthUriBuilder.Normalize(code);
        if (normalized is null)
        {
            return UserResult.Failure("Verification code is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserResult.Failure(UserNotFoundError);
        }

        var verified = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            normalized);
        if (!verified)
        {
            return UserResult.Failure("Verification code is invalid.");
        }

        var enable = await _userManager.SetTwoFactorEnabledAsync(user, true);
        return enable.Succeeded ? UserResult.Success(user.Id) : ToFailure(enable, "Failed to enable two-factor authentication.");
    }

    /// <inheritdoc />
    public async Task<UserResult> DisableTwoFactorAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserResult.Failure(UserNotFoundError);
        }

        var disable = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disable.Succeeded)
        {
            return ToFailure(disable, "Failed to disable two-factor authentication.");
        }
        // Wipe the shared key entirely so a stolen QR cannot validate after
        // disable. RemoveAuthenticationTokenAsync deletes the token row;
        // ResetAuthenticatorKeyAsync would replace it with a fresh secret,
        // which would silently be ready to use the moment 2FA is flipped on.
        await _userManager.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
        return UserResult.Success(user.Id);
    }

    /// <inheritdoc />
    public async Task<bool> IsTwoFactorEnabledAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        return user is not null && await _userManager.GetTwoFactorEnabledAsync(user);
    }

    /// <inheritdoc />
    public async Task<UserResult> GenerateRecoveryCodesAsync(
        string userId,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserResult.Failure(UserNotFoundError);
        }

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return UserResult.Failure("Two-factor authentication must be enabled before generating recovery codes.");
        }

        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, count);
        if (codes is null)
        {
            return UserResult.Failure("Failed to generate recovery codes.");
        }

        var list = codes.ToArray();
        return UserResult.Success(user.Id, new Dictionary<string, string>
        {
            ["recoveryCodes"] = string.Join('\n', list),
        });
    }

    /// <inheritdoc />
    public async Task<SignInResult> TwoFactorAuthenticatorSignInAsync(
        string code,
        bool persistent,
        bool rememberMachine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = OtpAuthUriBuilder.Normalize(code);
        if (normalized is null)
        {
            return SignInResult.InvalidCredentials();
        }

        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(normalized, persistent, rememberMachine);
        return await ToVisuAuthResultAsync(result);
    }

    /// <inheritdoc />
    public async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(code))
        {
            return SignInResult.InvalidCredentials();
        }
        // Recovery codes are stored verbatim (Identity ships them shaped as
        // "abcde-fghij"), so we only strip whitespace — stripping dashes the
        // way OtpAuthUriBuilder.Normalize does for TOTP would break the
        // lookup against the stored code.
        var normalized = code.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return SignInResult.InvalidCredentials();
        }

        var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(normalized);
        return await ToVisuAuthResultAsync(result);
    }

    private async Task<SignInResult> ToVisuAuthResultAsync(IdentitySignInResult result)
    {
        if (result.Succeeded)
        {
            // Identity does not surface the user id on a successful 2FA sign-in,
            // so look it up via the partial 2FA principal so callers can mint a JWT
            // or build a return URL.
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            return user is null ? SignInResult.Success(string.Empty) : SignInResult.Success(user.Id);
        }
        if (result.IsLockedOut)
        {
            return SignInResult.LockedOut();
        }
        if (result.IsNotAllowed)
        {
            return SignInResult.NotAllowed();
        }
        return SignInResult.InvalidCredentials();
    }

    private static UserResult ToFailure(IdentityResult result, string fallback)
    {
        var messages = result.Errors.Select(e => e.Description).ToList();
        return UserResult.Failure(
            messages.Count == 0 ? fallback : messages[0],
            messages);
    }
}

/// <summary>
/// Configures the issuer label embedded in <c>otpauth://</c> URIs and shown
/// inside authenticator apps. Defaults to <c>"VisuAuth"</c> so the consumer
/// can always rebrand without breaking existing enrolments.
/// </summary>
public sealed class TwoFactorIssuerOptions
{
    /// <summary>Issuer name (e.g. <c>Acme Corp</c>).</summary>
    public string Issuer { get; set; } = "VisuAuth";
}
