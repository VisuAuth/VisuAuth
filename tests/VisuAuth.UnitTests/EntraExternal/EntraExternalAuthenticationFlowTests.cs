using FluentAssertions;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.EntraExternal;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal;

/// <summary>
/// Verifies that every IAuthenticationFlow surface on the External
/// adapter returns the "Microsoft owns this" signal — the end-user pages
/// branch on capabilities first, but these defaults are the safety net
/// for callers that bypass the UI (CLI / direct POST / mobile app).
/// </summary>
public sealed class EntraExternalAuthenticationFlowTests
{
    private readonly EntraExternalAuthenticationFlow _sut = new();

    [Fact]
    public async Task SignInWithPasswordAsync_AlwaysReturnsRedirectToExternalProvider()
    {
        var result = await _sut.SignInWithPasswordAsync("a@b.com", "pwd", persistent: true, CancellationToken.None);

        result.Outcome.Should().Be(SignInOutcome.RedirectToExternalProvider,
            "the sign-in mapper interprets this outcome as 'show the Microsoft button hint' on the page");
        result.Error.Should().Contain("Microsoft",
            "the human-readable banner helps direct-API users understand the redirect");
    }

    [Fact]
    public async Task RegisterAsync_ReturnsFailure_UntilPRCWiresTheHostedFlow()
    {
        // v0.3 PR-B keeps RegisterAsync as a graceful failure for direct
        // API callers. PR-C will redirect to the hosted Microsoft signup
        // page via Microsoft.Identity.Web; until then this safety net
        // prevents a silent "success with no user created" outcome.
        var result = await _sut.RegisterAsync("a@b.com", "pwd", tenantId: null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RequestPasswordReset_And_ResetPassword_BothFail()
    {
        (await _sut.RequestPasswordResetAsync("a@b.com", CancellationToken.None)).IsSuccess.Should().BeFalse();
        (await _sut.ResetPasswordAsync("a@b.com", "tk", "newPwd", CancellationToken.None)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmEmail_Fails_BecauseMicrosoftValidatesDuringHostedSignup()
    {
        var result = await _sut.ConfirmEmailAsync("u-1", "tk", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SignOutAsync_IsNoOp_AndCompletes()
    {
        // SignOutAsync intentionally completes silently — the real sign-out
        // happens through the OIDC / cookie middleware Microsoft.Identity.Web
        // sets up in PR-C. VisuAuth's role here is just "don't crash".
        // Assert IsCompletedSuccessfully so Sonar (S2699) sees an explicit
        // expectation rather than treating "no throw" as implicit-pass.
        var task = _sut.SignOutAsync(CancellationToken.None);
        await task;
        task.IsCompletedSuccessfully.Should().BeTrue("the shim must finish without throwing or being cancelled");
    }

    [Fact]
    public void Capabilities_AlignsWithEntraExternalCapabilitiesSingleton()
    {
        _sut.Capabilities.Should().BeSameAs(EntraExternalCapabilities.Value,
            "single source of truth — the flow and the user store must agree byte-for-byte");
    }
}
