using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;
using Xunit;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Unit coverage for branches of <see cref="AspNetIdentityExternalLoginFlow{TUser}"/>
/// that integration tests can't reach without real lockout / not-allowed
/// state, or without driving the <c>/confirm</c> POST end-to-end.
/// </summary>
public sealed class AspNetIdentityExternalLoginFlowTests
{
    private readonly Mock<UserManager<IdentityUser>> _userManager;
    private readonly Mock<SignInManager<IdentityUser>> _signInManager;
    private readonly AspNetIdentityExternalLoginFlow<IdentityUser> _flow;

    public AspNetIdentityExternalLoginFlowTests()
    {
        _userManager = MockUserManager();
        _signInManager = MockSignInManager(_userManager.Object);
        _flow = new AspNetIdentityExternalLoginFlow<IdentityUser>(
            _signInManager.Object,
            _userManager.Object);
    }

    [Fact]
    public void Capabilities_AdvertisesLocalLoginAndExternalProviders()
    {
        _flow.Capabilities.SupportsLocalLogin.Should().BeTrue();
        _flow.Capabilities.SupportsExternalProviders.Should().BeTrue();
    }

    [Fact]
    public async Task GetProvidersAsync_MapsSchemesToProviderInfoAndPreservesDisplayName()
    {
        _signInManager
            .Setup(m => m.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(new[]
            {
                new AuthenticationScheme("Google", "Google", typeof(StubAuthenticationHandler)),
                new AuthenticationScheme("Microsoft", "Microsoft Account", typeof(StubAuthenticationHandler)),
            }.AsEnumerable());

        var providers = await _flow.GetProvidersAsync();

        providers.Should().HaveCount(2);
        providers.Should().ContainSingle(p => p.Scheme == "Google" && p.DisplayName == "Google");
        providers.Should().ContainSingle(p => p.Scheme == "Microsoft" && p.DisplayName == "Microsoft Account");
    }

    [Fact]
    public async Task GetProvidersAsync_FallsBackToSchemeNameWhenDisplayNameIsEmpty()
    {
        _signInManager
            .Setup(m => m.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(new[]
            {
                new AuthenticationScheme("Quirky", displayName: null, handlerType: typeof(StubAuthenticationHandler)),
            }.AsEnumerable());

        var providers = await _flow.GetProvidersAsync();

        providers.Single().DisplayName.Should().Be("Quirky",
            "missing display name must fall back to the scheme name so the button is never blank");
    }

    [Fact]
    public async Task CompleteSignInAsync_WithoutExternalCookie_ReturnsNoExternalSession()
    {
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync((ExternalLoginInfo?)null);

        var result = await _flow.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.NoExternalSession);
    }

    [Fact]
    public async Task CompleteSignInAsync_WhenLinkedUserIsLockedOut_MapsToLockedOut()
    {
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo("alice@example.com"));
        _signInManager
            .Setup(m => m.ExternalLoginSignInAsync("Google", "key", false, true))
            .ReturnsAsync(IdentitySignInResult.LockedOut);

        var result = await _flow.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.LockedOut);
    }

    [Fact]
    public async Task CompleteSignInAsync_WhenLinkedUserIsNotAllowed_MapsToNotAllowed()
    {
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo("alice@example.com"));
        _signInManager
            .Setup(m => m.ExternalLoginSignInAsync("Google", "key", false, true))
            .ReturnsAsync(IdentitySignInResult.NotAllowed);

        var result = await _flow.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.NotAllowed);
    }

    [Fact]
    public async Task CompleteSignInAsync_AlreadyLinkedUser_SignsInAndReturnsUserId()
    {
        var existing = new IdentityUser { Id = "u1", Email = "alice@example.com" };
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo("alice@example.com"));
        _signInManager
            .Setup(m => m.ExternalLoginSignInAsync("Google", "key", false, true))
            .ReturnsAsync(IdentitySignInResult.Success);
        _userManager
            .Setup(m => m.FindByLoginAsync("Google", "key"))
            .ReturnsAsync(existing);

        var result = await _flow.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.Success);
        result.UserId.Should().Be("u1");
    }

    [Fact]
    public async Task CompleteSignInAsync_AutoCreate_WithProviderEmailMissing_FallsThroughToConfirmation()
    {
        // Provider returned no email — AutoCreate cannot proceed without one,
        // so the adapter falls through to the confirmation page so the user
        // can supply an email manually.
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo(email: null));
        _signInManager
            .Setup(m => m.ExternalLoginSignInAsync("Google", "key", false, true))
            .ReturnsAsync(IdentitySignInResult.Failed);

        var result = await _flow.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.RequiresConfirmation,
            "AutoCreate with no email must escalate to /confirm so the user can supply one");
        result.PendingProvider.Should().Be("Google");
        result.PendingProviderKey.Should().Be("key");
    }

    [Fact]
    public async Task ConfirmAndCreateAsync_WithoutExternalCookie_ReturnsNoExternalSession()
    {
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync((ExternalLoginInfo?)null);

        var result = await _flow.ConfirmAndCreateAsync("alice@example.com", null, null);

        result.Outcome.Should().Be(ExternalSignInOutcome.NoExternalSession);
    }

    [Fact]
    public async Task ConfirmAndCreateAsync_WhenEmailMatchesExistingUser_LinksLoginAndReturnsSuccess()
    {
        var existing = new IdentityUser { Id = "u1", Email = "alice@example.com" };
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo("alice@example.com"));
        _userManager
            .Setup(m => m.FindByEmailAsync("alice@example.com"))
            .ReturnsAsync(existing);
        _userManager
            .Setup(m => m.AddLoginAsync(existing, It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _flow.ConfirmAndCreateAsync("alice@example.com", null, null);

        result.Outcome.Should().Be(ExternalSignInOutcome.Success);
        result.UserId.Should().Be("u1");
    }

    [Fact]
    public async Task ConfirmAndCreateAsync_LinkingExistingUserFails_ReturnsFailedWithIdentityErrors()
    {
        var existing = new IdentityUser { Id = "u1", Email = "alice@example.com" };
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo("alice@example.com"));
        _userManager
            .Setup(m => m.FindByEmailAsync("alice@example.com"))
            .ReturnsAsync(existing);
        _userManager
            .Setup(m => m.AddLoginAsync(existing, It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "X", Description = "already linked" }));

        var result = await _flow.ConfirmAndCreateAsync("alice@example.com", null, null);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().ContainSingle().Which.Should().Be("already linked");
    }

    [Fact]
    public async Task ConfirmAndCreateAsync_CreatingNewUserFails_ReturnsFailedWithIdentityErrors()
    {
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo("brand-new@example.com"));
        _userManager
            .Setup(m => m.FindByEmailAsync("brand-new@example.com"))
            .ReturnsAsync((IdentityUser?)null);
        _userManager
            .Setup(m => m.CreateAsync(It.IsAny<IdentityUser>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "Y", Description = "weak password policy" }));

        var result = await _flow.ConfirmAndCreateAsync("brand-new@example.com", "Brand", null);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().ContainSingle().Which.Should().Be("weak password policy");
    }

    [Fact]
    public async Task ConfirmAndCreateAsync_AddLoginAfterCreateFails_ReturnsFailed()
    {
        _signInManager
            .Setup(m => m.GetExternalLoginInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(BuildExternalLoginInfo("brand-new@example.com"));
        _userManager
            .Setup(m => m.FindByEmailAsync("brand-new@example.com"))
            .ReturnsAsync((IdentityUser?)null);
        _userManager
            .Setup(m => m.CreateAsync(It.IsAny<IdentityUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManager
            .Setup(m => m.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "Z", Description = "duplicate login" }));

        var result = await _flow.ConfirmAndCreateAsync("brand-new@example.com", "Brand", null);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().ContainSingle().Which.Should().Be("duplicate login");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConfirmAndCreateAsync_WithBlankEmail_Throws(string? email)
    {
        var act = () => _flow.ConfirmAndCreateAsync(email!, null, null);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(email));
    }

    private static ExternalLoginInfo BuildExternalLoginInfo(string? email)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "key"),
        };
        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }
        var identity = new ClaimsIdentity(claims, "TestExternal");
        var principal = new ClaimsPrincipal(identity);
        return new ExternalLoginInfo(principal, "Google", "key", "Google");
    }

    private static Mock<UserManager<IdentityUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<IdentityUser>>();
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.SetupGet(o => o.Value).Returns(new IdentityOptions());
        return new Mock<UserManager<IdentityUser>>(
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
    }

    private static Mock<SignInManager<IdentityUser>> MockSignInManager(UserManager<IdentityUser> userManager)
    {
        var contextAccessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<IdentityUser>>();
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.SetupGet(o => o.Value).Returns(new IdentityOptions());
        var schemes = new Mock<IAuthenticationSchemeProvider>();
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

    /// <summary>Implements <see cref="IAuthenticationHandler"/> only so
    /// <see cref="AuthenticationScheme"/>'s ctor accepts it as the handler
    /// type. Never instantiated; the tests inspect scheme metadata only.</summary>
    private sealed class StubAuthenticationHandler : IAuthenticationHandler
    {
        public Task<AuthenticateResult> AuthenticateAsync() => throw new NotImplementedException();
        public Task ChallengeAsync(AuthenticationProperties? properties) => throw new NotImplementedException();
        public Task ForbidAsync(AuthenticationProperties? properties) => throw new NotImplementedException();
        public Task InitializeAsync(AuthenticationScheme scheme, Microsoft.AspNetCore.Http.HttpContext context) => Task.CompletedTask;
    }
}
