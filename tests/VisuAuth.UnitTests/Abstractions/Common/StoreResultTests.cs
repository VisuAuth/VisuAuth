using FluentAssertions;
using VisuAuth.Abstractions.Common;
using Xunit;

namespace VisuAuth.UnitTests.Abstractions.Common;

public sealed class StoreResultTests
{
    [Fact]
    public void Success_DefaultArguments_ReturnsSuccessfulResultWithNullResourceIdAndEmptyMetadata()
    {
        var result = StoreResult.Success();

        result.IsSuccess.Should().BeTrue();
        result.ResourceId.Should().BeNull();
        result.Error.Should().BeNull();
        result.ValidationErrors.Should().BeEmpty();
        result.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void Success_WithResourceIdAndMetadata_RoundTripsBothValues()
    {
        var metadata = new Dictionary<string, string>
        {
            ["temporaryPassword"] = "Pa$$1",
            ["resetToken"] = "abc",
        };

        var result = StoreResult.Success("user-42", metadata);

        result.IsSuccess.Should().BeTrue();
        result.ResourceId.Should().Be("user-42");
        result.Metadata.Should().BeEquivalentTo(metadata);
    }

    [Fact]
    public void Failure_WithErrorOnly_HasIsSuccessFalseAndEmptyValidationErrors()
    {
        var result = StoreResult.Failure("Something broke.");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Something broke.");
        result.ValidationErrors.Should().BeEmpty();
        result.ResourceId.Should().BeNull();
    }

    [Fact]
    public void Failure_WithValidationErrors_PreservesEveryEntry()
    {
        var errors = new[] { "Email is required.", "Password too short." };

        var result = StoreResult.Failure("Validation failed.", errors);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Validation failed.");
        result.ValidationErrors.Should().BeEquivalentTo(errors);
    }
}
