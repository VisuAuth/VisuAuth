using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using VisuAuth.Entra.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Lock-down for <see cref="EntraOptions"/>'s default + validation
/// shape. The DI extension wires <c>ValidateDataAnnotations</c> +
/// <c>ValidateOnStart</c>, so missing TenantId / ClientId / ClientSecret
/// fails the app at startup — these tests pin the [Required] decorations
/// so a careless refactor can't silently defer the failure to runtime.
/// </summary>
public sealed class EntraOptionsTests
{
    [Fact]
    public void GraphBaseUrl_DefaultsToPublicCloud_V10()
    {
        new EntraOptions().GraphBaseUrl
            .Should().Be("https://graph.microsoft.com/v1.0",
                "public-cloud v1.0 is the right default — sovereign clouds (US Gov / China) opt in via override");
    }

    [Fact]
    public void Validation_AllRequiredFieldsMissing_FailsWithThreeErrors()
    {
        var opts = new EntraOptions();
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Select(r => r.MemberNames.First()).Should().BeEquivalentTo(
            new[] { nameof(EntraOptions.TenantId), nameof(EntraOptions.ClientId), nameof(EntraOptions.ClientSecret) },
            "the [Required] trio is what AddVisuAuthEntra(...).ValidateDataAnnotations() enforces at startup");
    }

    [Fact]
    public void Validation_AllRequiredFieldsPresent_Passes()
    {
        var opts = new EntraOptions
        {
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            ClientSecret = "abc",
        };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void AppRoleResourceId_IsOptional_DefaultsToNull()
    {
        new EntraOptions().AppRoleResourceId.Should().BeNull(
            "AppRoleResourceId defaults to ClientId at use-site — the option exists so multi-app deployments can point the role catalogue at a different target than the registered app");
    }
}
