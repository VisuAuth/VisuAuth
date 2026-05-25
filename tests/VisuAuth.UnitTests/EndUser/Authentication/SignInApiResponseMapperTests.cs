using FluentAssertions;
using Microsoft.AspNetCore.Http;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.EndUserUi.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.Authentication;

/// <summary>
/// Lock-down for the HTTP status policy on the minimal-API login
/// endpoint. Each outcome maps to a deliberate status code: 423 (Locked)
/// only for explicit lockouts, 403 for NotAllowed (so the client can
/// distinguish "do something to enable" from a generic auth failure),
/// 401 for everything else (including unknown email — anti-enumeration).
/// </summary>
public sealed class SignInApiResponseMapperTests
{
    [Theory]
    [InlineData(SignInOutcome.LockedOut, StatusCodes.Status423Locked)]
    [InlineData(SignInOutcome.RequiresTwoFactor, StatusCodes.Status401Unauthorized)]
    [InlineData(SignInOutcome.NotAllowed, StatusCodes.Status403Forbidden)]
    [InlineData(SignInOutcome.InvalidCredentials, StatusCodes.Status401Unauthorized)]
    [InlineData(SignInOutcome.RedirectToExternalProvider, StatusCodes.Status401Unauthorized)]
    public void MapFailure_ProducesExpectedStatusCode(SignInOutcome outcome, int expectedStatus)
    {
        var result = SignInApiResponseMapper.MapFailure(new SignInResult { Outcome = outcome });

        // IResult exposes the status code through IStatusCodeHttpResult on
        // every Results.Json shape. Cast and assert.
        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(expectedStatus);
    }
}
