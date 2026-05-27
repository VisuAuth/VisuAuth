using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using VisuAuth.EntraExternal.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal;

/// <summary>
/// Lock-down for <see cref="EntraExternalOptions"/>'s defaults +
/// validation shape. The DI extension wires
/// <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c>, so a missing
/// [Required] field fails the app at startup — these tests pin the
/// decorations so a careless refactor can't silently defer the failure
/// to runtime.
/// </summary>
public sealed class EntraExternalOptionsTests
{
    [Fact]
    public void GraphBaseUrl_DefaultsToPublicCloud_V10()
    {
        new EntraExternalOptions().GraphBaseUrl
            .Should().Be("https://graph.microsoft.com/v1.0",
                "public-cloud v1.0 is the right default for External; sovereign clouds opt in via override");
    }

    [Fact]
    public void Validation_AllRequiredFieldsMissing_FailsWithFourErrors()
    {
        // External adds TenantDomain to the [Required] trio Workforce has
        // — the identities[] payload needs it on Create. Pinning the count
        // here catches a regression where someone drops the attribute.
        var opts = new EntraExternalOptions();
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Select(r => r.MemberNames.First()).Should().BeEquivalentTo(
            new[]
            {
                nameof(EntraExternalOptions.TenantId),
                nameof(EntraExternalOptions.ClientId),
                nameof(EntraExternalOptions.ClientSecret),
                nameof(EntraExternalOptions.TenantDomain),
            },
            "the [Required] quartet is what AddVisuAuthEntraExternal(...).ValidateDataAnnotations() enforces at startup");
    }

    [Fact]
    public void Validation_AllRequiredFieldsPresent_Passes()
    {
        var opts = new EntraExternalOptions
        {
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            ClientSecret = "abc",
            TenantDomain = "contoso.onmicrosoft.com",
        };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void AppRoleResourceId_IsOptional_DefaultsToNull()
    {
        new EntraExternalOptions().AppRoleResourceId.Should().BeNull(
            "AppRoleResourceId defaults to ClientId at use-site — exists so multi-app deployments can target a different app's role catalogue");
    }

    [Fact]
    public void DefaultEmailDomain_IsOptional_DefaultsToNull()
    {
        new EntraExternalOptions().DefaultEmailDomain.Should().BeNull(
            "External is permissive — customers sign up with any email domain by default, the UI suggests one only when configured");
    }
}
