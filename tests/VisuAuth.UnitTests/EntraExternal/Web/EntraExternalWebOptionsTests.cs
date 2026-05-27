using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using VisuAuth.EntraExternal.Web.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal.Web;

/// <summary>
/// Lock-down for <see cref="EntraExternalWebOptions"/>'s defaults +
/// validation shape. The DI extension wires
/// <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c>, so a missing
/// required field fails the app at startup — these tests pin which
/// fields are required so a careless refactor can't silently defer the
/// failure to runtime.
/// </summary>
public sealed class EntraExternalWebOptionsTests
{
    [Fact]
    public void CallbackPath_DefaultsToSignInOidc_TheMicrosoftIdentityWebConvention()
    {
        new EntraExternalWebOptions().CallbackPath.Should().Be("/signin-oidc",
            "Microsoft.Identity.Web's default redirect path is /signin-oidc — matching it minimises consumer setup");
    }

    [Fact]
    public void SignedOutCallbackPath_DefaultsToSignOutCallbackOidc()
    {
        new EntraExternalWebOptions().SignedOutCallbackPath.Should().Be("/signout-callback-oidc");
    }

    [Fact]
    public void Validation_AllRequiredFieldsMissing_FailsWithThreeErrors()
    {
        // TenantSubdomain, TenantId, ClientId are the [Required] trio.
        // ClientSecret is optional (public clients), SignedOutCallbackPath
        // has a default, SignInUserFlow is reserved for PR D.
        var opts = new EntraExternalWebOptions();
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Select(r => r.MemberNames.First()).Should().BeEquivalentTo(
            new[]
            {
                nameof(EntraExternalWebOptions.TenantSubdomain),
                nameof(EntraExternalWebOptions.TenantId),
                nameof(EntraExternalWebOptions.ClientId),
            },
            "the [Required] trio is what AddVisuAuthEntraExternalSignIn(...).ValidateDataAnnotations() enforces at startup");
    }

    [Fact]
    public void Validation_AllRequiredFieldsPresent_Passes()
    {
        var opts = new EntraExternalWebOptions
        {
            TenantSubdomain = "contoso",
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
        };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true)
            .Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void GetAuthority_BuildsTheCiamLoginUrl_WithTheTenantIdAndV2Path()
    {
        // External authority shape: https://{tenant}.ciamlogin.com/{tenant-id}/v2.0
        // (NOT login.microsoftonline.com — that's the Workforce authority).
        // Microsoft.Identity.Web uses this URL to fetch the OpenID config
        // document; a typo here is the single most common "OIDC not
        // working" symptom.
        var opts = new EntraExternalWebOptions
        {
            TenantSubdomain = "contoso",
            TenantId = "11111111-2222-3333-4444-555555555555",
            ClientId = "client",
        };

        opts.GetAuthority().Should().Be(
            "https://contoso.ciamlogin.com/11111111-2222-3333-4444-555555555555/v2.0",
            "External authority is {tenant}.ciamlogin.com + tenant id + v2.0 — distinct from Workforce's login.microsoftonline.com");
    }

    [Fact]
    public void ClientSecret_IsOptional_DefaultsToNull()
    {
        new EntraExternalWebOptions().ClientSecret.Should().BeNull(
            "public client registrations don't need a secret — leaving it nullable lets that path work");
    }
}
