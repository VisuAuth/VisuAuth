using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Abstractions.Users;
using VisuAuth.Identity.Users;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Users;

/// <summary>
/// Unit coverage for the synchronous defences of
/// <see cref="AspNetIdentityUserStore{TUser}"/>: the blank-email guard on
/// create and the user-not-found branch every mutating method shares. The
/// happy paths are exercised end-to-end by the integration suite; these
/// branches are otherwise unreachable without artificial Identity state.
/// </summary>
public sealed class AspNetIdentityUserStoreTests
{
    private readonly Mock<UserManager<IdentityUser>> _userManager;
    private readonly AspNetIdentityUserStore<IdentityUser> _store;

    public AspNetIdentityUserStoreTests()
    {
        _userManager = MockUserManager();
        _store = new AspNetIdentityUserStore<IdentityUser>(_userManager.Object, TimeProvider.System);
    }

    [Fact]
    public async Task CreateAsync_WithBlankEmail_ReturnsFailureBeforeTouchingIdentity()
    {
        var result = await _store.CreateAsync(new CreateUserCommand { Email = "   " });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Email is required.");
        _userManager.Verify(
            m => m.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()),
            Times.Never,
            "the blank-email guard must short-circuit before Identity is called");
    }

    [Fact]
    public async Task UpdateAsync_WhenUserNotFound_ReturnsNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.UpdateAsync("missing", new UpdateUserCommand());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("missing").And.Contain("not found");
    }

    [Fact]
    public async Task DeleteAsync_WhenUserNotFound_ReturnsNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.DeleteAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task SetEnabledAsync_WhenUserNotFound_ReturnsNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.SetEnabledAsync("missing", enabled: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenUserNotFound_ReturnsNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.ResetPasswordAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ResetTwoFactorAsync_WhenUserNotFound_ReturnsNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.ResetTwoFactorAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RevokeSessionsAsync_WhenUserNotFound_ReturnsNotFoundFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.RevokeSessionsAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
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
}
