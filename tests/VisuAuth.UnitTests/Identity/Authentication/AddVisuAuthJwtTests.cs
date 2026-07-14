using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.Identity.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Registration-time guards for <see cref="JwtServiceCollectionExtensions.AddVisuAuthJwt{TUser}"/>.
/// The HS256 key-length check runs for the primary key and every rotation key
/// so a misconfiguration surfaces at startup, not deep in the middleware.
/// </summary>
public sealed class AddVisuAuthJwtTests
{
    private const string ValidKey = "a-valid-signing-key-of-at-least-32-utf8-bytes!!!";

    [Fact]
    public void AddVisuAuthJwt_WithShortAdditionalValidationKey_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddVisuAuthJwt<IdentityUser>(options =>
        {
            options.SigningKey = ValidKey;
            options.AdditionalValidationKeys.Add("too-short");
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AdditionalValidationKeys*");
    }

    [Fact]
    public void AddVisuAuthJwt_WithShortPrimaryKey_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddVisuAuthJwt<IdentityUser>(options =>
        {
            options.SigningKey = "too-short";
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SigningKey*");
    }

    [Fact]
    public void AddVisuAuthJwt_WithValidKeys_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddVisuAuthJwt<IdentityUser>(options =>
        {
            options.SigningKey = ValidKey;
            options.AdditionalValidationKeys.Add(ValidKey + "-rotated");
        });

        act.Should().NotThrow();
    }
}
