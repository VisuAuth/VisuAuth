using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.EndUserUi;
using VisuAuth.EndUserUi.Pages.ExternalLogin;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.ExternalLogin;

/// <summary>
/// Direct unit coverage for <see cref="CallbackModel"/>'s OAuth-landing
/// switch. Each <see cref="ExternalSignInOutcome"/> arm writes a distinct
/// audit shape and produces a distinct redirect; the integration tests
/// only flex the AutoCreate happy path, so the other arms (LockedOut /
/// NotAllowed / NoExternalSession / RequiresConfirmation / Failed /
/// remoteError) are otherwise uncovered.
/// </summary>
public sealed class ExternalLoginCallbackModelTests
{
    [Fact]
    public async Task OnGet_WhenRemoteErrorPresent_AuditsFailureAndRedirectsBackToLogin()
    {
        var (page, flow, audit) = Build();
        page.RemoteError = "access_denied";

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/visuauth/login");
        audit.Verify(a => a.WriteAsync(
            It.Is<AuditEvent>(e => e.Action == AuditActions.ExternalLoginFailed
                && e.Outcome == AuditOutcome.Failure
                && e.FailureReason == "access_denied"
                && e.Payload!["source"] == "remoteError"),
            It.IsAny<CancellationToken>()), Times.Once);
        flow.Verify(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()),
            Times.Never, "remote error short-circuits — we never call the flow");
    }

    [Fact]
    public async Task OnGet_OnSuccess_AuditsAndRedirectsToReturnUrlOrRoot()
    {
        var (page, flow, audit) = Build();
        flow.Setup(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult
            {
                Outcome = ExternalSignInOutcome.Success,
                UserId = "u-1",
                PendingEmail = "u@e.com",
                PendingProvider = "Google",
            });

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/",
            "ReturnUrl is null so SanitiseLocalReturnUrl falls back to root");
        audit.Verify(a => a.WriteAsync(
            It.Is<AuditEvent>(e => e.Action == AuditActions.ExternalLoginSucceeded
                && e.Outcome == AuditOutcome.Success
                && e.TargetId == "u-1"
                && e.Payload!["provider"] == "Google"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGet_OnRequiresConfirmation_RedirectsToConfirmPageWithoutAudit()
    {
        var (page, flow, audit) = Build();
        flow.Setup(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult { Outcome = ExternalSignInOutcome.RequiresConfirmation });

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/visuauth/external-login/confirm",
                "no returnUrl was supplied, so we hand off to confirm without a query string");
        audit.Verify(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()),
            Times.Never, "RequiresConfirmation is a hop, not an outcome — the confirm page audits its own result");
    }

    [Fact]
    public async Task OnGet_OnNoExternalSession_AuditsFailureAndRedirectsToLogin()
    {
        var (page, flow, audit) = Build();
        flow.Setup(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult { Outcome = ExternalSignInOutcome.NoExternalSession });

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/visuauth/login");
        audit.Verify(a => a.WriteAsync(
            It.Is<AuditEvent>(e => e.Action == AuditActions.ExternalLoginFailed
                && e.FailureReason == "No external session"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGet_OnLockedOut_AuditsLockoutWithProviderPayload_AndRedirectsToLogin()
    {
        var (page, flow, audit) = Build();
        flow.Setup(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult
            {
                Outcome = ExternalSignInOutcome.LockedOut,
                UserId = "u-9",
                PendingEmail = "x@y.com",
                PendingProvider = "GitHub",
            });

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/visuauth/login");
        audit.Verify(a => a.WriteAsync(
            It.Is<AuditEvent>(e => e.Action == AuditActions.LoginLockedOut
                && e.Outcome == AuditOutcome.Failure
                && e.TargetId == "u-9"
                && e.Payload!["provider"] == "GitHub"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGet_OnNotAllowed_AuditsAsExternalLoginFailedWithReason_AndRedirectsToLogin()
    {
        var (page, flow, audit) = Build();
        flow.Setup(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult
            {
                Outcome = ExternalSignInOutcome.NotAllowed,
                UserId = "u-3",
                PendingEmail = "x@y.com",
                PendingProvider = "Microsoft",
            });

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/visuauth/login");
        audit.Verify(a => a.WriteAsync(
            It.Is<AuditEvent>(e => e.Action == AuditActions.ExternalLoginFailed
                && e.FailureReason == "Sign-in not allowed"
                && e.TargetId == "u-3"
                && e.Payload!["provider"] == "Microsoft"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGet_OnFailedWithBackendErrors_AuditsConcatenatedReason_AndRedirectsToLogin()
    {
        var (page, flow, audit) = Build();
        flow.Setup(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult
            {
                Outcome = ExternalSignInOutcome.Failed,
                PendingProvider = "Apple",
                Errors = ["Email claim missing", "Could not link account"],
            });

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/visuauth/login");
        audit.Verify(a => a.WriteAsync(
            It.Is<AuditEvent>(e => e.Action == AuditActions.ExternalLoginFailed
                && e.FailureReason == "Email claim missing; Could not link account"
                && e.Payload!["provider"] == "Apple"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnGet_OnFailedWithNoBackendErrors_FallsBackToLocalisedGenericReason()
    {
        var (page, flow, audit) = Build();
        flow.Setup(f => f.CompleteSignInAsync(It.IsAny<ExternalLoginFirstTimeStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult { Outcome = ExternalSignInOutcome.Failed, Errors = [] });

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/visuauth/login");
        audit.Verify(a => a.WriteAsync(
            It.Is<AuditEvent>(e => e.Action == AuditActions.ExternalLoginFailed
                && e.FailureReason!.Contains("signinfailed")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CurrentTenantId_WhenMultiTenancyOff_IsNull()
    {
        var (page, _, _) = Build();
        page.CurrentTenantId.Should().BeNull(
            "tenant-context.IsMultiTenancyEnabled is false in the default mock");
    }

    [Fact]
    public void CurrentTenantId_WhenMultiTenancyOn_ReturnsContextValue()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsMultiTenancyEnabled).Returns(true);
        tenant.SetupGet(t => t.CurrentTenantId).Returns("acme");

        var (page, _, _) = Build(tenant: tenant);

        page.CurrentTenantId.Should().Be("acme");
    }

    private static (CallbackModel page, Mock<IExternalLoginFlow> flow, Mock<IAuditWriter> audit) Build(
        Mock<ITenantContext>? tenant = null)
    {
        var flow = new Mock<IExternalLoginFlow>();
        var jwt = new Mock<IJwtIssuer>();
        tenant ??= new Mock<ITenantContext>();
        var external = Options.Create(new ExternalLoginOptions());
        var webView = Options.Create(new WebViewCallbackOptions());
        var audit = new Mock<IAuditWriter>();
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var localizer = StubLocalizer();

        var httpContext = new DefaultHttpContext();
        var page = new CallbackModel(
            flow.Object,
            jwt.Object,
            tenant.Object,
            external,
            webView,
            audit.Object,
            localizer)
        {
            PageContext = new PageContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor(),
                new ModelStateDictionary())),
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>()),
        };
        page.Url = new UrlHelper(page.PageContext);
        return (page, flow, audit);
    }

    private static IStringLocalizer<EndUserSharedResources> StubLocalizer()
    {
        var loc = new Mock<IStringLocalizer<EndUserSharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()])
           .Returns((string key) => new LocalizedString(key, key.ToLowerInvariant().Replace('.', ' ').Replace('_', ' ')));
        return loc.Object;
    }
}
