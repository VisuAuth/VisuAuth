using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Moq;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.EndUserUi;
using VisuAuth.EndUserUi.Pages.TwoFactor;
using VisuAuth.EndUserUi.TwoFactor;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.TwoFactor;

/// <summary>
/// Direct unit coverage for the capability-disabled "two-factor not
/// supported" branches on the three TOTP page models. These are
/// otherwise unreachable from integration tests (the live ASP.NET
/// Identity adapter always reports <c>SupportsTwoFactor = true</c>),
/// but matter for a future Entra adapter that opts out of TOTP.
/// </summary>
public sealed class TwoFactorPageDefensiveBranchTests
{
    private static readonly UserBackendCapabilities NotSupported = new()
    {
        SupportsTwoFactor = false,
    };

    [Fact]
    public async Task SetupModel_OnGet_WhenTwoFactorNotSupported_RendersPageWithNotSupportedError()
    {
        var page = BuildSetup(NotSupported, out var localizer);

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>("the page must render with an explanatory error, not redirect");
        page.ErrorMessage.Should().Contain("notsupported", BecauseLocalizerStub(localizer));
    }

    [Fact]
    public async Task SetupModel_OnPostVerify_WhenTwoFactorNotSupported_RendersPageWithNotSupportedError()
    {
        var page = BuildSetup(NotSupported, out _);
        page.VerificationCode = "123456";

        var result = await page.OnPostVerifyAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.ErrorMessage.Should().Contain("notsupported");
    }

    [Fact]
    public async Task SetupModel_OnPostResetKey_WhenTwoFactorNotSupported_RendersPageWithNotSupportedError()
    {
        var page = BuildSetup(NotSupported, out _);

        var result = await page.OnPostResetKeyAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.ErrorMessage.Should().Contain("notsupported");
    }

    [Fact]
    public void VerifyModel_OnGet_WhenTwoFactorNotSupported_RendersPageWithGlobalError()
    {
        var page = BuildVerify(NotSupported);

        var result = page.OnGet();

        result.Should().BeOfType<PageResult>();
        page.GlobalError.Should().Contain("notsupported");
    }

    [Fact]
    public async Task VerifyModel_OnPostAuthenticator_WhenTwoFactorNotSupported_RendersPageWithGlobalError()
    {
        var page = BuildVerify(NotSupported);
        page.Form = new VerifyModel.ChallengeForm { Code = "123456" };

        var result = await page.OnPostAuthenticatorAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.GlobalError.Should().Contain("notsupported");
    }

    [Fact]
    public async Task VerifyModel_OnPostRecovery_WhenTwoFactorNotSupported_RendersPageWithGlobalError()
    {
        var page = BuildVerify(NotSupported);
        page.Form = new VerifyModel.ChallengeForm { RecoveryCode = "abc-def" };

        var result = await page.OnPostRecoveryAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.GlobalError.Should().Contain("notsupported");
    }

    [Fact]
    public async Task VerifyModel_OnPostAuthenticator_WithBlankCode_RendersAuthenticatorErrorWithoutCallingFlow()
    {
        var twoFactor = new Mock<ITwoFactorFlow>(MockBehavior.Strict);
        twoFactor.SetupGet(f => f.Capabilities).Returns(SupportedCapabilities());
        var localizer = StubLocalizer();
        var page = new VerifyModel(twoFactor.Object, localizer)
        {
            Form = new VerifyModel.ChallengeForm { Code = "   " },
        };

        var result = await page.OnPostAuthenticatorAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.AuthenticatorError.Should().Contain("coderequired");
        // Strict mock fails the test if the flow was called.
    }

    [Fact]
    public async Task VerifyModel_OnPostRecovery_WithBlankCode_RendersRecoveryErrorAndOpensDetails()
    {
        var twoFactor = new Mock<ITwoFactorFlow>(MockBehavior.Strict);
        twoFactor.SetupGet(f => f.Capabilities).Returns(SupportedCapabilities());
        var localizer = StubLocalizer();
        var page = new VerifyModel(twoFactor.Object, localizer)
        {
            Form = new VerifyModel.ChallengeForm { RecoveryCode = "" },
        };

        var result = await page.OnPostRecoveryAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.RecoveryError.Should().Contain("recoveryrequired");
        page.RecoveryDetailsOpen.Should().BeTrue("a blank recovery submit must keep the disclosure open");
    }

    [Fact]
    public async Task RecoveryCodesModel_OnPostGenerate_WhenTwoFactorNotSupported_RendersPageWithError()
    {
        var page = BuildRecoveryCodes(NotSupported);

        var result = await page.OnPostGenerateAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.ErrorMessage.Should().Contain("notsupported");
    }

    [Fact]
    public async Task RecoveryCodesModel_OnPostDisable_WhenTwoFactorNotSupported_RendersPageWithError()
    {
        var page = BuildRecoveryCodes(NotSupported);

        var result = await page.OnPostDisableAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.ErrorMessage.Should().Contain("notsupported");
    }

    [Fact]
    public async Task RecoveryCodesModel_OnGet_WhenTwoFactorNotSupported_RendersPageWithError()
    {
        var page = BuildRecoveryCodes(NotSupported);

        var result = await page.OnGetAsync(generated: false, CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.ErrorMessage.Should().Contain("notsupported");
    }

    private static SetupModel BuildSetup(UserBackendCapabilities caps, out IStringLocalizer<EndUserSharedResources> localizer)
    {
        var flow = new Mock<ITwoFactorFlow>();
        flow.SetupGet(f => f.Capabilities).Returns(caps);
        localizer = StubLocalizer();
        var renderer = new Mock<IQrCodeSvgRenderer>().Object;
        return new SetupModel(flow.Object, renderer, localizer);
    }

    private static VerifyModel BuildVerify(UserBackendCapabilities caps)
    {
        var flow = new Mock<ITwoFactorFlow>();
        flow.SetupGet(f => f.Capabilities).Returns(caps);
        return new VerifyModel(flow.Object, StubLocalizer());
    }

    private static RecoveryCodesModel BuildRecoveryCodes(UserBackendCapabilities caps)
    {
        var flow = new Mock<ITwoFactorFlow>();
        flow.SetupGet(f => f.Capabilities).Returns(caps);
        return new RecoveryCodesModel(flow.Object, StubLocalizer());
    }

    private static UserBackendCapabilities SupportedCapabilities() => new() { SupportsTwoFactor = true };

    /// <summary>
    /// Returns a localizer that echoes the key as the value with the dotted
    /// prefix stripped — keeps assertions readable ("two-factor" matches
    /// "TwoFactor.NotSupported", "code" matches "TwoFactor.Setup.Error.CodeRequired").
    /// </summary>
    private static IStringLocalizer<EndUserSharedResources> StubLocalizer()
    {
        var loc = new Mock<IStringLocalizer<EndUserSharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, EchoFor(key)));
        return loc.Object;
    }

    private static string EchoFor(string key) => key.ToLowerInvariant().Replace('.', ' ').Replace('_', ' ');

    private static string BecauseLocalizerStub(IStringLocalizer<EndUserSharedResources> localizer)
        => "the stub localizer echoes the resource key, so the message contains 'two-factor' from the canonical 'TwoFactor.NotSupported' key";
}
