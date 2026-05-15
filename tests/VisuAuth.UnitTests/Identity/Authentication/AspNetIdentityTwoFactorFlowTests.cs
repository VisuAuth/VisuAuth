using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;
using Xunit;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using SignInResult = VisuAuth.Abstractions.Authentication.SignInResult;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Unit coverage for the failure branches of <see cref="AspNetIdentityTwoFactorFlow{TUser}"/>.
/// The happy paths are exercised end-to-end by integration tests; here we
/// pin the user-not-found, lockout, and not-allowed branches that are
/// otherwise unreachable without artificial Identity state.
/// </summary>
public sealed class AspNetIdentityTwoFactorFlowTests
{
    private readonly Mock<UserManager<IdentityUser>> _userManager;
    private readonly Mock<SignInManager<IdentityUser>> _signInManager;
    private readonly AspNetIdentityTwoFactorFlow<IdentityUser> _flow;

    public AspNetIdentityTwoFactorFlowTests()
    {
        _userManager = MockUserManager();
        _signInManager = MockSignInManager(_userManager.Object);
        _flow = new AspNetIdentityTwoFactorFlow<IdentityUser>(
            _userManager.Object,
            _signInManager.Object,
            Options.Create(new TwoFactorIssuerOptions { Issuer = "VisuAuth.Tests" }));
    }

    [Fact]
    public async Task GetAuthenticatorSetupAsync_WhenUserNotFound_ReturnsNull()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var setup = await _flow.GetAuthenticatorSetupAsync("missing");

        setup.Should().BeNull();
    }

    [Fact]
    public async Task GetAuthenticatorSetupAsync_WhenResetReturnsNullKey_ReturnsNull()
    {
        var user = new IdentityUser { Id = "u1", Email = "alice@example.com" };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _userManager.SetupSequence(m => m.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync((string?)null)
            .ReturnsAsync((string?)null);
        _userManager.Setup(m => m.ResetAuthenticatorKeyAsync(user)).ReturnsAsync(IdentityResult.Success);

        var setup = await _flow.GetAuthenticatorSetupAsync("u1");

        setup.Should().BeNull("the lazy-reset path must surface as null when the key still cannot be generated");
    }

    [Fact]
    public async Task ResetAuthenticatorKeyAsync_WhenUserNotFound_ReturnsFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _flow.ResetAuthenticatorKeyAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("User not found.");
    }

    [Fact]
    public async Task ResetAuthenticatorKeyAsync_WhenResetReportsIdentityErrors_SurfacesFirstErrorMessage()
    {
        var user = new IdentityUser { Id = "u1" };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _userManager.Setup(m => m.SetTwoFactorEnabledAsync(user, false)).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.ResetAuthenticatorKeyAsync(user)).ReturnsAsync(
            IdentityResult.Failed(new IdentityError { Code = "X", Description = "boom" }));

        var result = await _flow.ResetAuthenticatorKeyAsync("u1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("boom");
        result.ValidationErrors.Should().ContainSingle().Which.Should().Be("boom");
    }

    [Fact]
    public async Task EnableTwoFactorAsync_WithBlankCode_ReturnsCodeRequiredFailure()
    {
        var result = await _flow.EnableTwoFactorAsync("u1", "   ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Verification code is required.");
    }

    [Fact]
    public async Task EnableTwoFactorAsync_WhenUserNotFound_ReturnsUserNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _flow.EnableTwoFactorAsync("missing", "123456");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("User not found.");
    }

    [Fact]
    public async Task DisableTwoFactorAsync_WhenUserNotFound_ReturnsUserNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _flow.DisableTwoFactorAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("User not found.");
    }

    [Fact]
    public async Task DisableTwoFactorAsync_WhenSetTwoFactorFails_ReturnsFailureWithoutTouchingTokens()
    {
        var user = new IdentityUser { Id = "u1" };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _userManager.Setup(m => m.SetTwoFactorEnabledAsync(user, false)).ReturnsAsync(
            IdentityResult.Failed(new IdentityError { Code = "X", Description = "denied" }));

        var result = await _flow.DisableTwoFactorAsync("u1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("denied");
        _userManager.Verify(
            m => m.RemoveAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "the key must NOT be wiped if disabling 2FA failed — leaving the user mid-toggle is a worse state");
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_WhenUserNotFound_ReturnsUserNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _flow.GenerateRecoveryCodesAsync("missing", 10);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("User not found.");
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_WhenTwoFactorDisabled_ReturnsExplanatoryFailure()
    {
        var user = new IdentityUser { Id = "u1" };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _userManager.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(false);

        var result = await _flow.GenerateRecoveryCodesAsync("u1", 10);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Two-factor authentication must be enabled");
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_WhenIdentityReturnsNull_ReturnsGenerationFailure()
    {
        var user = new IdentityUser { Id = "u1" };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _userManager.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _userManager.Setup(m => m.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))
            .ReturnsAsync((IEnumerable<string>?)null);

        var result = await _flow.GenerateRecoveryCodesAsync("u1", 10);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Failed to generate recovery codes.");
    }

    [Fact]
    public async Task TwoFactorAuthenticatorSignInAsync_WithBlankCode_ReturnsInvalidCredentialsWithoutCallingIdentity()
    {
        var result = await _flow.TwoFactorAuthenticatorSignInAsync("   ", persistent: true, rememberMachine: false);

        result.Outcome.Should().Be(SignInOutcome.InvalidCredentials);
        _signInManager.Verify(
            m => m.TwoFactorAuthenticatorSignInAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task TwoFactorAuthenticatorSignInAsync_WhenLockedOut_MapsToLockedOut()
    {
        _signInManager
            .Setup(m => m.TwoFactorAuthenticatorSignInAsync("123456", true, false))
            .ReturnsAsync(IdentitySignInResult.LockedOut);

        var result = await _flow.TwoFactorAuthenticatorSignInAsync("123456", persistent: true, rememberMachine: false);

        result.Outcome.Should().Be(SignInOutcome.LockedOut);
    }

    [Fact]
    public async Task TwoFactorAuthenticatorSignInAsync_WhenNotAllowed_MapsToNotAllowed()
    {
        _signInManager
            .Setup(m => m.TwoFactorAuthenticatorSignInAsync("123456", false, false))
            .ReturnsAsync(IdentitySignInResult.NotAllowed);

        var result = await _flow.TwoFactorAuthenticatorSignInAsync("123456", persistent: false, rememberMachine: false);

        result.Outcome.Should().Be(SignInOutcome.NotAllowed);
    }

    [Fact]
    public async Task TwoFactorAuthenticatorSignInAsync_WhenSucceedsButPartialCookieMissing_ReturnsSuccessWithEmptyUserId()
    {
        _signInManager
            .Setup(m => m.TwoFactorAuthenticatorSignInAsync("123456", true, false))
            .ReturnsAsync(IdentitySignInResult.Success);
        _signInManager
            .Setup(m => m.GetTwoFactorAuthenticationUserAsync())
            .ReturnsAsync((IdentityUser?)null);

        var result = await _flow.TwoFactorAuthenticatorSignInAsync("123456", persistent: true, rememberMachine: false);

        result.Outcome.Should().Be(SignInOutcome.Success);
        result.UserId.Should().BeEmpty(
            "the partial cookie is normally still in scope on success, but the adapter must not throw when it isn't");
    }

    [Fact]
    public async Task TwoFactorRecoveryCodeSignInAsync_WithBlankCode_ReturnsInvalidCredentials()
    {
        var result = await _flow.TwoFactorRecoveryCodeSignInAsync("   ");

        result.Outcome.Should().Be(SignInOutcome.InvalidCredentials);
        _signInManager.Verify(
            m => m.TwoFactorRecoveryCodeSignInAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task TwoFactorRecoveryCodeSignInAsync_StripsWhitespaceButKeepsDashes()
    {
        // Recovery codes ship as "abcde-fghij" — Identity stores them
        // verbatim, so the adapter must NOT strip the dash.
        _signInManager
            .Setup(m => m.TwoFactorRecoveryCodeSignInAsync("abcde-fghij"))
            .ReturnsAsync(IdentitySignInResult.Success);
        _signInManager
            .Setup(m => m.GetTwoFactorAuthenticationUserAsync())
            .ReturnsAsync(new IdentityUser { Id = "u1" });

        var result = await _flow.TwoFactorRecoveryCodeSignInAsync("  abcde-fghij  ");

        result.Outcome.Should().Be(SignInOutcome.Success);
        result.UserId.Should().Be("u1");
        _signInManager.Verify(
            m => m.TwoFactorRecoveryCodeSignInAsync("abcde-fghij"),
            Times.Once,
            "only whitespace is stripped — the dash is part of the stored code");
    }

    [Fact]
    public void Capabilities_ExposesTwoFactorAndItsAdminReset()
    {
        _flow.Capabilities.SupportsTwoFactor.Should().BeTrue();
        _flow.Capabilities.SupportsTwoFactorReset.Should().BeTrue();
        _flow.Capabilities.SupportsLocalLogin.Should().BeTrue();
    }

    private static Mock<UserManager<IdentityUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<IdentityUser>>();
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.SetupGet(o => o.Value).Returns(new IdentityOptions());
        var mgr = new Mock<UserManager<IdentityUser>>(
            store.Object,
            optionsAccessor.Object,
            new PasswordHasher<IdentityUser>(),
            Array.Empty<IUserValidator<IdentityUser>>(),
            Array.Empty<IPasswordValidator<IdentityUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            new Mock<Microsoft.Extensions.Logging.ILogger<UserManager<IdentityUser>>>().Object)
        {
            CallBase = false,
        };
        return mgr;
    }

    private static Mock<SignInManager<IdentityUser>> MockSignInManager(UserManager<IdentityUser> userManager)
    {
        var contextAccessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<IdentityUser>>();
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.SetupGet(o => o.Value).Returns(new IdentityOptions());
        var schemes = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        var confirmation = new Mock<IUserConfirmation<IdentityUser>>();
        return new Mock<SignInManager<IdentityUser>>(
            userManager,
            contextAccessor.Object,
            claimsFactory.Object,
            optionsAccessor.Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<SignInManager<IdentityUser>>>().Object,
            schemes.Object,
            confirmation.Object)
        {
            CallBase = false,
        };
    }
}
