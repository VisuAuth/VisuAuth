using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.EndUserUi;
using VisuAuth.EndUserUi.Pages.ExternalLogin;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.ExternalLogin;

/// <summary>
/// Direct unit coverage for <see cref="ConfirmModel"/>'s three switch
/// arms (Success / NoExternalSession / Failed) plus the early-bail
/// branches. Integration tests only exercise the AutoCreate path; the
/// confirm-form scenarios are otherwise unreachable without a real
/// OAuth round-trip.
/// </summary>
public sealed class ExternalLoginConfirmModelTests
{
    [Fact]
    public async Task OnGet_WhenPendingInfoIsNull_RedirectsToLogin()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalPendingInfo?)null);

        var page = Build(flow);

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/visuauth/login",
                "no external cookie means the user has to start over from the login page");
    }

    [Fact]
    public async Task OnGet_WhenPendingInfoPresent_PopulatesFormAndRendersPage()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalPendingInfo
            {
                Provider = "Google",
                ProviderKey = "pk",
                Email = "user@example.com",
                DisplayName = "User Name",
            });

        var page = Build(flow);

        var result = await page.OnGetAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.ProviderDisplayName.Should().Be("Google");
        page.Form.Email.Should().Be("user@example.com");
        page.Form.UserName.Should().BeNull(
            "UserName is left blank so the adapter falls back to email — see Confirm.cshtml.cs comment");
    }

    [Fact]
    public async Task OnPost_WhenPendingInfoIsNull_RedirectsToLogin()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalPendingInfo?)null);

        var page = Build(flow);

        var result = await page.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/visuauth/login");
    }

    [Fact]
    public async Task OnPost_WhenEmailIsBlank_RendersPageWithEmailRequiredError()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalPendingInfo { Provider = "Google", ProviderKey = "pk", Email = null });

        var page = Build(flow);
        page.Form = new ConfirmModel.ConfirmForm { Email = "   ", UserName = "alice" };

        var result = await page.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.Errors.Should().ContainSingle().Which.Should().Contain("emailrequired");
    }

    [Fact]
    public async Task OnPost_OnSuccess_WritesAuditAndRedirectsToRoot_WhenReturnUrlEmpty()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalPendingInfo { Provider = "Google", ProviderKey = "pk", Email = "u@e.com" });
        flow.Setup(f => f.ConfirmAndCreateAsync(
                "u@e.com", "alice", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult
            {
                Outcome = ExternalSignInOutcome.Success,
                UserId = "u-7",
                PendingEmail = "u@e.com",
                PendingProvider = "Google",
            });

        var audit = new Mock<IAuditWriter>();
        AuditEvent? captured = null;
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
             .Callback<AuditEvent, CancellationToken>((e, _) => captured = e)
             .Returns(Task.CompletedTask);

        var page = Build(flow, audit: audit);
        page.Form = new ConfirmModel.ConfirmForm { Email = "u@e.com", UserName = "alice" };
        // ReturnUrl left null → SanitiseLocalReturnUrl short-circuits to "/"
        // without needing a configured Url helper.

        var result = await page.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/");
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.ExternalLoginAutoCreated);
        captured.Outcome.Should().Be(AuditOutcome.Success);
        captured.TargetId.Should().Be("u-7");
        captured.Payload!["provider"].Should().Be("Google");
        captured.Payload["strategy"].Should().Be("ConfirmAndCreate");
    }

    [Fact]
    public async Task OnPost_OnNoExternalSession_WritesAuditFailureAndRendersPage()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalPendingInfo { Provider = "GitHub", ProviderKey = "pk", Email = "x@y.com" });
        flow.Setup(f => f.ConfirmAndCreateAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult { Outcome = ExternalSignInOutcome.NoExternalSession });

        var audit = new Mock<IAuditWriter>();
        AuditEvent? captured = null;
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
             .Callback<AuditEvent, CancellationToken>((e, _) => captured = e)
             .Returns(Task.CompletedTask);

        var page = Build(flow, audit: audit);
        page.Form = new ConfirmModel.ConfirmForm { Email = "x@y.com" };

        var result = await page.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.Errors.Should().ContainSingle().Which.Should().Contain("nosession");
        captured!.Action.Should().Be(AuditActions.ExternalLoginFailed);
        captured.FailureReason.Should().Be("No external session");
        captured.Payload!["provider"].Should().Be("GitHub");
    }

    [Fact]
    public async Task OnPost_OnFailedWithBackendErrors_PropagatesErrorsAndWritesAuditFailure()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalPendingInfo { Provider = "Microsoft", ProviderKey = "pk", Email = "x@y.com" });
        flow.Setup(f => f.ConfirmAndCreateAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult
            {
                Outcome = ExternalSignInOutcome.Failed,
                Errors = ["Email already taken", "Username invalid"],
            });

        var audit = new Mock<IAuditWriter>();
        AuditEvent? captured = null;
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
             .Callback<AuditEvent, CancellationToken>((e, _) => captured = e)
             .Returns(Task.CompletedTask);

        var page = Build(flow, audit: audit);
        page.Form = new ConfirmModel.ConfirmForm { Email = "x@y.com" };

        var result = await page.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.Errors.Should().HaveCount(2);
        captured!.FailureReason.Should().Be("Email already taken; Username invalid",
            "the audit reason concatenates so operators see every validation failure in one cell");
    }

    [Fact]
    public async Task OnPost_OnFailedWithNoBackendErrors_FallsBackToLocalisedGenericError()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalPendingInfo { Provider = "Microsoft", ProviderKey = "pk", Email = "x@y.com" });
        flow.Setup(f => f.ConfirmAndCreateAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInResult
            {
                Outcome = ExternalSignInOutcome.Failed,
                Errors = [],
            });

        var page = Build(flow);
        page.Form = new ConfirmModel.ConfirmForm { Email = "x@y.com" };

        var result = await page.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        page.Errors.Should().ContainSingle().Which.Should().Contain("signinfailed");
    }

    [Fact]
    public async Task OnPost_WhenMultiTenancyEnabled_PassesCurrentTenantIdThrough()
    {
        var flow = new Mock<IExternalLoginFlow>();
        flow.Setup(f => f.GetPendingInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalPendingInfo { Provider = "Google", ProviderKey = "pk", Email = "u@e.com" });
        string? capturedTenant = null;
        flow.Setup(f => f.ConfirmAndCreateAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, CancellationToken>((_, _, t, _) => capturedTenant = t)
            .ReturnsAsync(new ExternalSignInResult { Outcome = ExternalSignInOutcome.Success, UserId = "u-1" });

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsMultiTenancyEnabled).Returns(true);
        tenant.SetupGet(t => t.CurrentTenantId).Returns("tenant-7");

        var page = Build(flow, tenant: tenant);
        page.Form = new ConfirmModel.ConfirmForm { Email = "u@e.com" };

        await page.OnPostAsync(CancellationToken.None);

        capturedTenant.Should().Be("tenant-7",
            "the new account must be scoped to the current tenant or it would leak across tenants");
    }

    private static ConfirmModel Build(
        Mock<IExternalLoginFlow> flow,
        Mock<ITenantContext>? tenant = null,
        Mock<IAuditWriter>? audit = null)
    {
        tenant ??= new Mock<ITenantContext>();
        audit ??= new Mock<IAuditWriter>();
        var localizer = StubLocalizer();

        var page = new ConfirmModel(flow.Object, tenant.Object, audit.Object, localizer)
        {
            PageContext = new PageContext(new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new ActionDescriptor(),
                new ModelStateDictionary())),
        };
        page.Url = new UrlHelper(page.PageContext);
        return page;
    }

    private static IStringLocalizer<EndUserSharedResources> StubLocalizer()
    {
        var loc = new Mock<IStringLocalizer<EndUserSharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()])
           .Returns((string key) => new LocalizedString(key, key.ToLowerInvariant().Replace('.', ' ').Replace('_', ' ')));
        return loc.Object;
    }
}
