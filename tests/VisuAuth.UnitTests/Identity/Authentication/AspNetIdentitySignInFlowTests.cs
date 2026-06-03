using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Identity.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Unit coverage for the not-found branch of
/// <see cref="AspNetIdentitySignInFlow{TUser}"/>'s password-reset completion.
/// The happy path is exercised end-to-end by the integration suite; the
/// "no user for this email" branch is pinned here so the deliberately vague
/// "Invalid reset link." message (no account enumeration) cannot regress.
/// </summary>
public sealed class AspNetIdentitySignInFlowTests
{
    private readonly Mock<UserManager<IdentityUser>> _userManager;
    private readonly AspNetIdentitySignInFlow<IdentityUser> _flow;

    public AspNetIdentitySignInFlowTests()
    {
        _userManager = MockUserManager();
        var signInManager = MockSignInManager(_userManager.Object);
        _flow = new AspNetIdentitySignInFlow<IdentityUser>(signInManager.Object, _userManager.Object);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenEmailHasNoAccount_ReturnsInvalidResetLink()
    {
        _userManager.Setup(m => m.FindByEmailAsync("ghost@example.com")).ReturnsAsync((IdentityUser?)null);

        var result = await _flow.ResetPasswordAsync("ghost@example.com", "token", "New-Passw0rd!");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid reset link.",
            "an unknown email must not reveal whether the account exists");
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
