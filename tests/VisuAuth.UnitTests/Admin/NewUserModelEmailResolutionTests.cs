using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;
using VisuAuth.AdminUi;
using VisuAuth.AdminUi.Pages.Admin.Users;
using Xunit;

namespace VisuAuth.UnitTests.Admin;

/// <summary>
/// Pins how the create-user page assembles the final email address from the
/// local-part input plus the chosen domain. The branch matters because the
/// multi-domain dropdown, the single locked suffix, and free-text entry all
/// feed the same <c>Form.Email</c> field, and a tampered POST must never let
/// an arbitrary domain through.
/// </summary>
public sealed class NewUserModelEmailResolutionTests
{
    private static readonly string[] DomainChoices = ["contoso.com", "contoso.onmicrosoft.com"];

    [Fact]
    public async Task OnPost_MultiDomainSelection_CombinesLocalPartWithChosenDomain()
    {
        CreateUserCommand? captured = null;
        var page = BuildPage(
            new UserBackendCapabilities { SupportsRegistration = true, EmailDomainSuffix = "@contoso.com" },
            DomainChoices,
            cmd => captured = cmd);
        page.Form.Email = "alice";
        page.Form.EmailDomain = "contoso.onmicrosoft.com";

        await page.OnPostAsync(CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Email.Should().Be("alice@contoso.onmicrosoft.com",
            "a valid dropdown choice wins over the default suffix");
    }

    [Fact]
    public async Task OnPost_TamperedDomainNotInChoices_FallsBackToSuffix()
    {
        CreateUserCommand? captured = null;
        var page = BuildPage(
            new UserBackendCapabilities { SupportsRegistration = true, EmailDomainSuffix = "@contoso.com" },
            DomainChoices,
            cmd => captured = cmd);
        page.Form.Email = "alice";
        page.Form.EmailDomain = "evil.example";

        await page.OnPostAsync(CancellationToken.None);

        captured!.Email.Should().Be("alice@contoso.com",
            "a domain that isn't an offered choice is ignored — the suffix is used instead");
    }

    [Fact]
    public async Task OnPost_FullAddressTyped_IsNeverReappended()
    {
        CreateUserCommand? captured = null;
        var page = BuildPage(
            new UserBackendCapabilities { SupportsRegistration = true, EmailDomainSuffix = "@contoso.com" },
            DomainChoices,
            cmd => captured = cmd);
        page.Form.Email = "external@partner.com";
        page.Form.EmailDomain = "contoso.com";

        await page.OnPostAsync(CancellationToken.None);

        captured!.Email.Should().Be("external@partner.com",
            "a value already containing '@' is a full address and must pass through untouched");
    }

    [Fact]
    public async Task OnPost_SingleSuffixNoDomainSource_AppendsSuffix()
    {
        CreateUserCommand? captured = null;
        var page = BuildPage(
            new UserBackendCapabilities { SupportsRegistration = true, EmailDomainSuffix = "@contoso.com" },
            emailDomains: null,
            cmd => captured = cmd);
        page.Form.Email = "bob";

        await page.OnPostAsync(CancellationToken.None);

        captured!.Email.Should().Be("bob@contoso.com",
            "with no domain source the existing locked-suffix behaviour is preserved");
    }

    private static NewModel BuildPage(
        UserBackendCapabilities capabilities,
        string[]? emailDomains,
        Action<CreateUserCommand> onCreate)
    {
        var userStore = new Mock<IUserStore>();
        userStore.SetupGet(s => s.Capabilities).Returns(capabilities);
        userStore.Setup(s => s.CreateAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateUserCommand, CancellationToken>((cmd, _) => onCreate(cmd))
            .ReturnsAsync(UserResult.Success("new-user-id"));

        var roleStore = new Mock<IRoleStore>();
        roleStore.Setup(s => s.ListAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var localizer = new Mock<IStringLocalizer<AdminSharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));

        IEmailDomainSource? domainSource = null;
        if (emailDomains is not null)
        {
            var src = new Mock<IEmailDomainSource>();
            src.Setup(s => s.GetEmailDomainsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(emailDomains);
            domainSource = src.Object;
        }

        var page = new NewModel(
            userStore.Object,
            roleStore.Object,
            new Mock<IAuditWriter>().Object,
            localizer.Object,
            domainSource);

        // OnPost calls Redirect(...) on the happy path, so the page never
        // renders a partial — but wire the minimal PageContext anyway so any
        // re-render branch (e.g. role errors) doesn't NRE.
        var metadataProvider = new EmptyModelMetadataProvider();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new PageActionDescriptor(),
            modelState);
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(metadataProvider, modelState),
        };
        return page;
    }
}
