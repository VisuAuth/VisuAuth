using FluentAssertions;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.EndUserUi.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.Authentication;

/// <summary>
/// Lock-down for the Razor login page's outcome interpretation. Each
/// non-Success outcome that bounces the user back to the form must
/// produce a resource key the localiser can resolve.
/// </summary>
public sealed class SignInPageResponseMapperTests
{
    [Theory]
    [InlineData(SignInOutcome.Success, SignInPageDecision.RedirectSuccess)]
    [InlineData(SignInOutcome.RequiresTwoFactor, SignInPageDecision.RedirectTwoFactor)]
    [InlineData(SignInOutcome.LockedOut, SignInPageDecision.RenderPage)]
    [InlineData(SignInOutcome.NotAllowed, SignInPageDecision.RenderPage)]
    [InlineData(SignInOutcome.RedirectToExternalProvider, SignInPageDecision.RenderPage)]
    [InlineData(SignInOutcome.InvalidCredentials, SignInPageDecision.RenderPage)]
    public void Map_ProducesExpectedDecision(SignInOutcome outcome, SignInPageDecision expected)
    {
        var page = SignInPageResponseMapper.Map(new SignInResult { Outcome = outcome });
        page.Decision.Should().Be(expected);
    }

    [Fact]
    public void Map_LockedOut_CarriesLockedResourceKey()
    {
        var page = SignInPageResponseMapper.Map(new SignInResult { Outcome = SignInOutcome.LockedOut });
        page.ErrorResourceKey.Should().Be("Login.Error.Locked");
        page.RawErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Map_NotAllowed_PropagatesRawErrorMessage_WhenPresent()
    {
        var page = SignInPageResponseMapper.Map(new SignInResult
        {
            Outcome = SignInOutcome.NotAllowed,
            Error = "Email not confirmed",
        });

        page.RawErrorMessage.Should().Be("Email not confirmed",
            "the backend-specific reason wins over the generic resource key so the user sees what to do next");
    }

    [Fact]
    public void Map_RedirectSuccess_HasNoErrorKey()
    {
        var page = SignInPageResponseMapper.Map(new SignInResult { Outcome = SignInOutcome.Success });
        page.ErrorResourceKey.Should().BeNull();
        page.RawErrorMessage.Should().BeNull();
    }
}
