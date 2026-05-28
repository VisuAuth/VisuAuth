using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.AdminUi;
using VisuAuth.AdminUi.Pages.Admin.ExternalProviders;
using Xunit;

namespace VisuAuth.UnitTests.Admin;

/// <summary>
/// Pins the "config infrastructure not wired" behaviour of the External
/// Providers admin page. In Entra / Entra External deployments (Microsoft
/// owns the providers) the consumer never registers
/// <c>IExternalProviderConfigStore</c> + friends, so the page must
/// activate with nulls and render the "not available" card — NOT 500 on
/// DI activation with "Unable to resolve service for type
/// IExternalProviderConfigStore", which is the bug this fixes.
/// </summary>
public sealed class ExternalProvidersIndexModelUnavailableTests
{
    [Fact]
    public void ProviderConfigAvailable_IsFalse_WhenConfigStoreNotInjected()
    {
        var page = BuildPage();
        page.ProviderConfigAvailable.Should().BeFalse(
            "no IExternalProviderConfigStore in DI → the page is in the read-only-unavailable state");
    }

    [Fact]
    public async Task OnGet_WhenInfraMissing_RendersWithoutThrowing_AndLeavesListsEmpty()
    {
        var page = BuildPage();

        var result = await page.OnGetAsync(CancellationToken.None);

        // The whole point: activation + GET succeed instead of throwing the
        // unresolved-service exception the user hit on /admin/external-providers.
        result.Should().BeAssignableTo<IActionResult>();
        page.ActiveProviders.Should().BeEmpty();
        page.CustomProviders.Should().BeEmpty();
        page.AvailableProviders.Should().BeEmpty("LoadAsync is skipped when the infra is absent — nothing touches the null services");
        page.OrphanProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task OnPostSave_WhenInfraMissing_NoThrow_NoAudit()
    {
        // A crafted direct POST (the form is never rendered when
        // unavailable) must bail cleanly rather than NRE on the null store
        // or write a misleading audit row.
        var audit = new Mock<IAuditWriter>();
        var page = BuildPage(audit);

        var act = () => page.OnPostSaveAsync("Google", CancellationToken.None);

        await act.Should().NotThrowAsync();
        audit.Verify(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("BulkEnable")]
    [InlineData("BulkDisable")]
    public async Task OnPostBulk_WhenInfraMissing_NoThrow(string which)
    {
        var page = BuildPage();
        Func<Task> act = which == "BulkEnable"
            ? () => page.OnPostBulkEnableAsync(CancellationToken.None)
            : () => page.OnPostBulkDisableAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnPostDeleteOrphan_WhenInfraMissing_NoThrow()
    {
        var page = BuildPage();
        var act = () => page.OnPostDeleteOrphanAsync("Google", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnPostToggleEnabled_WhenInfraMissing_NoThrow()
    {
        var page = BuildPage();
        var act = () => page.OnPostToggleEnabledAsync("Google", true, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    private static IndexModel BuildPage(Mock<IAuditWriter>? audit = null)
    {
        var localizer = new Mock<IStringLocalizer<AdminSharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));

        // The four external-provider infra services are passed as null —
        // exactly what DI hands the page when the consumer never wired
        // AddVisuAuthExternalProviderConfigStore (Entra / Entra External).
        var page = new IndexModel(
            configStore: null,
            registry: null,
            cacheInvalidator: null,
            staticSnapshot: null,
            (audit ?? new Mock<IAuditWriter>()).Object,
            localizer.Object);

        var metadataProvider = new EmptyModelMetadataProvider();
        var tempDataFactory = new Mock<ITempDataDictionaryFactory>();
        tempDataFactory.Setup(f => f.GetTempData(It.IsAny<HttpContext>()))
            .Returns(Mock.Of<ITempDataDictionary>());

        var requestServices = new ServiceCollection();
        requestServices.AddSingleton<IModelMetadataProvider>(metadataProvider);
        requestServices.AddSingleton(tempDataFactory.Object);

        var modelState = new ModelStateDictionary();
        var httpContext = new DefaultHttpContext { RequestServices = requestServices.BuildServiceProvider() };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(metadataProvider, modelState),
        };
        return page;
    }
}
