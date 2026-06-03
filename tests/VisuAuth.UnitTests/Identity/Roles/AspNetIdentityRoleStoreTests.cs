using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Identity.Roles;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Roles;

/// <summary>
/// Unit coverage for the validation / not-found branches of
/// <see cref="AspNetIdentityRoleStore{TUser, TRole}"/>. The happy paths run
/// against a live Identity stack in the integration suite; these guard
/// clauses are pinned here where mocking the managers is cheap.
/// </summary>
public sealed class AspNetIdentityRoleStoreTests
{
    private readonly Mock<RoleManager<IdentityRole>> _roleManager;
    private readonly Mock<UserManager<IdentityUser>> _userManager;
    private readonly AspNetIdentityRoleStore<IdentityUser, IdentityRole> _store;

    public AspNetIdentityRoleStoreTests()
    {
        _roleManager = MockRoleManager();
        _userManager = MockUserManager();
        _store = new AspNetIdentityRoleStore<IdentityUser, IdentityRole>(_roleManager.Object, _userManager.Object);
    }

    [Fact]
    public async Task CreateAsync_WithBlankName_ReturnsFailure()
    {
        var result = await _store.CreateAsync("   ", tenantId: null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Role name is required.");
    }

    [Fact]
    public async Task CreateAsync_WhenIdentityFails_SurfacesFirstErrorMessage()
    {
        _roleManager.Setup(m => m.CreateAsync(It.IsAny<IdentityRole>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "X", Description = "duplicate role" }));

        var result = await _store.CreateAsync("Admin", tenantId: null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("duplicate role");
        result.ValidationErrors.Should().ContainSingle().Which.Should().Be("duplicate role");
    }

    [Fact]
    public async Task RenameAsync_WithBlankNewName_ReturnsFailure()
    {
        var result = await _store.RenameAsync("r1", "   ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("New role name is required.");
    }

    [Fact]
    public async Task RenameAsync_WhenRoleNotFound_ReturnsFailure()
    {
        _roleManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityRole?)null);

        var result = await _store.RenameAsync("missing", "NewName");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("missing").And.Contain("not found");
    }

    [Fact]
    public async Task DeleteAsync_WhenRoleNotFound_ReturnsFailure()
    {
        _roleManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityRole?)null);

        var result = await _store.DeleteAsync("missing");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task AssignRoleAsync_WithBlankRoleName_ReturnsFailure()
    {
        var result = await _store.AssignRoleAsync("u1", "   ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Role name is required.");
    }

    [Fact]
    public async Task AssignRoleAsync_WhenUserNotFound_ReturnsFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.AssignRoleAsync("missing", "Admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("missing").And.Contain("not found");
    }

    [Fact]
    public async Task AssignRoleAsync_WhenUserAlreadyInRole_ReturnsFailure()
    {
        var user = new IdentityUser { Id = "u1" };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _roleManager.Setup(m => m.RoleExistsAsync("Admin")).ReturnsAsync(true);
        _userManager.Setup(m => m.IsInRoleAsync(user, "Admin")).ReturnsAsync(true);

        var result = await _store.AssignRoleAsync("u1", "Admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already in role");
    }

    [Fact]
    public async Task RemoveRoleAsync_WithBlankRoleName_ReturnsFailure()
    {
        var result = await _store.RemoveRoleAsync("u1", "   ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Role name is required.");
    }

    [Fact]
    public async Task RemoveRoleAsync_WhenUserNotFound_ReturnsFailure()
    {
        _userManager.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((IdentityUser?)null);

        var result = await _store.RemoveRoleAsync("missing", "Admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("missing").And.Contain("not found");
    }

    [Fact]
    public async Task RemoveRoleAsync_WhenUserNotInRole_ReturnsFailure()
    {
        var user = new IdentityUser { Id = "u1" };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _userManager.Setup(m => m.IsInRoleAsync(user, "Admin")).ReturnsAsync(false);

        var result = await _store.RemoveRoleAsync("u1", "Admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not in role");
    }

    private static Mock<RoleManager<IdentityRole>> MockRoleManager()
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        return new Mock<RoleManager<IdentityRole>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<Microsoft.Extensions.Logging.ILogger<RoleManager<IdentityRole>>>().Object)
        {
            CallBase = false,
        };
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
