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
using VisuAuth.AdminUi.Pages.Admin.Roles;
using Xunit;

namespace VisuAuth.UnitTests.Admin;

/// <summary>
/// Pins the defence-in-depth catch on the admin Roles page: when the
/// backend's <see cref="IRoleStore"/> throws
/// <see cref="NotSupportedException"/> from create / rename / delete
/// (the Microsoft Graph adapters do, because app roles are
/// manifest-declared), the handler must surface the friendly
/// "managed by the identity provider" message and re-render the
/// catalogue partial — NOT let the exception bubble up as a 500 in the
/// htmx swap. The UI hides these controls via
/// <see cref="UserBackendCapabilities.SupportsRoleMutation"/>, but a
/// stale form or a direct POST can still reach the handler.
/// </summary>
public sealed class RolesIndexModelNotSupportedTests
{
    private const string NotSupportedKey = "Roles.Error.MutationNotSupported";

    [Fact]
    public async Task OnPostCreate_WhenStoreThrowsNotSupported_ShowsFriendlyError_NoThrow()
    {
        var roleStore = BuildThrowingRoleStore();
        var page = BuildPage(roleStore.Object);
        page.NewRoleName = "Editor";

        var result = await page.OnPostCreateAsync(CancellationToken.None);

        result.Should().BeOfType<PartialViewResult>()
            .Which.ViewName.Should().Be("_RolesCatalogue", "the handler must re-render the catalogue, not 500");
        page.ActionErrors.Should().ContainSingle().Which.Should().Be(NotSupportedKey,
            "the operator needs the 'roles are managed by the identity provider' explanation, not a stack trace");
    }

    [Fact]
    public async Task OnPostRename_WhenStoreThrowsNotSupported_ShowsFriendlyError_NoThrow()
    {
        var roleStore = BuildThrowingRoleStore();
        var page = BuildPage(roleStore.Object);
        page.RenamedRoleName = "NewName";

        var result = await page.OnPostRenameAsync("role-1", CancellationToken.None);

        result.Should().BeOfType<PartialViewResult>();
        page.ActionErrors.Should().ContainSingle().Which.Should().Be(NotSupportedKey);
    }

    [Fact]
    public async Task OnPostDelete_WhenStoreThrowsNotSupported_ShowsFriendlyError_NoThrow()
    {
        var roleStore = BuildThrowingRoleStore();
        var page = BuildPage(roleStore.Object);

        var result = await page.OnPostDeleteAsync("role-1", CancellationToken.None);

        result.Should().BeOfType<PartialViewResult>();
        page.ActionErrors.Should().ContainSingle().Which.Should().Be(NotSupportedKey);
    }

    [Fact]
    public async Task OnPostCreate_NotSupported_DoesNotWriteAnAuditEvent()
    {
        // The unsupported path bails before the success/failure audit
        // write — there's no meaningful "RoleCreated failure" to record
        // when the backend never offered the operation. Verify we don't
        // emit a misleading audit row.
        var roleStore = BuildThrowingRoleStore();
        var audit = new Mock<IAuditWriter>();
        var page = BuildPage(roleStore.Object, audit);
        page.NewRoleName = "Editor";

        await page.OnPostCreateAsync(CancellationToken.None);

        audit.Verify(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IRoleStore> BuildThrowingRoleStore()
    {
        var store = new Mock<IRoleStore>();
        store.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("App roles are declared in the manifest."));
        store.Setup(s => s.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("App roles are declared in the manifest."));
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("App roles are declared in the manifest."));
        // GetAsync is called before Rename/Delete to capture the old name;
        // return a role so the handler reaches the throwing mutation call.
        store.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleSummary { Id = "role-1", Name = "Admin" });
        // LoadAsync re-renders the catalogue after the catch.
        store.Setup(s => s.ListAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return store;
    }

    private static IndexModel BuildPage(IRoleStore roleStore, Mock<IAuditWriter>? audit = null)
    {
        var userStore = new Mock<IUserStore>();
        // SupportsRoleMutation = false mirrors what an Entra-mode deploy
        // declares — the scenario where the handler can be reached with a
        // store that throws.
        userStore.SetupGet(s => s.Capabilities).Returns(new UserBackendCapabilities { SupportsRoleMutation = false });

        var localizer = new Mock<IStringLocalizer<AdminSharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));

        var page = new IndexModel(
            userStore.Object,
            roleStore,
            (audit ?? new Mock<IAuditWriter>()).Object,
            localizer.Object);

        // Partial(...) reads PageContext.ViewData and PageModel.TempData;
        // the latter resolves ITempDataDictionaryFactory off RequestServices.
        // Wire the minimal services so the handler's return-Partial doesn't
        // NRE — we only assert on the result type + ActionErrors, not on
        // rendered HTML.
        var metadataProvider = new EmptyModelMetadataProvider();
        var tempDataFactory = new Mock<ITempDataDictionaryFactory>();
        tempDataFactory.Setup(f => f.GetTempData(It.IsAny<HttpContext>()))
            .Returns(Mock.Of<ITempDataDictionary>());

        var requestServices = new ServiceCollection();
        requestServices.AddSingleton<IModelMetadataProvider>(metadataProvider);
        requestServices.AddSingleton(tempDataFactory.Object);

        var modelState = new ModelStateDictionary();
        var httpContext = new DefaultHttpContext { RequestServices = requestServices.BuildServiceProvider() };
        var actionContext = new ActionContext(
            httpContext,
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
