using FluentAssertions;
using Microsoft.Extensions.Localization;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Configuration;
using VisuAuth.AdminUi;
using VisuAuth.AdminUi.Pages.Admin.EntraConfig;
using Xunit;

namespace VisuAuth.UnitTests.Admin;

/// <summary>
/// Covers the adapter-config admin page model: the tri-state mapping from the
/// posted form to the store command (preserve / clear / set), firing the
/// change notifier so a save takes effect without a restart, and the guarantee
/// that secret plaintext never lands in the audit payload.
/// </summary>
public sealed class EntraConfigModelTests
{
    private const string Adapter = "Entra";

    [Fact]
    public async Task OnPostSave_BlankSecret_PreservesIt_AndSetsNonSecret()
    {
        var (page, store, _, _) = BuildPage();
        SaveAdapterConfigCommand? captured = null;
        store.Setup(s => s.SaveAsync(It.IsAny<SaveAdapterConfigCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SaveAdapterConfigCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(UserResult.Success());

        page.FieldValues["TenantId"] = "tenant-x";
        page.FieldValues["ClientSecret"] = string.Empty; // blank secret

        await page.OnPostSaveAsync(Adapter, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Values.Single(v => v.Key == "TenantId").Value.Should().Be("tenant-x");
        captured.Values.Single(v => v.Key == "ClientSecret").Value
            .Should().BeNull("a blank secret means preserve the existing value");
    }

    [Fact]
    public async Task OnPostSave_ClearSecretTicked_ClearsTheSecret()
    {
        var (page, store, _, _) = BuildPage();
        SaveAdapterConfigCommand? captured = null;
        store.Setup(s => s.SaveAsync(It.IsAny<SaveAdapterConfigCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SaveAdapterConfigCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(UserResult.Success());

        page.FieldValues["ClientSecret"] = string.Empty;
        page.ClearSecrets.Add("ClientSecret");

        await page.OnPostSaveAsync(Adapter, CancellationToken.None);

        captured!.Values.Single(v => v.Key == "ClientSecret").Value
            .Should().Be(string.Empty, "ticking clear maps to the empty-string clear sentinel");
    }

    [Fact]
    public async Task OnPostSave_Success_FiresNotifier_AndAuditOmitsSecretValues()
    {
        var (page, store, audit, notifier) = BuildPage();
        store.Setup(s => s.SaveAsync(It.IsAny<SaveAdapterConfigCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserResult.Success());
        AuditEvent? recorded = null;
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEvent, CancellationToken>((e, _) => recorded = e)
            .Returns(Task.CompletedTask);

        page.FieldValues["ClientSecret"] = "brand-new-secret";

        await page.OnPostSaveAsync(Adapter, CancellationToken.None);

        notifier.Fired.Should().BeTrue("a successful save must nudge the adapter to recompute its options");
        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be(AuditActions.AdapterConfigSaved);
        recorded.Outcome.Should().Be(AuditOutcome.Success);
        // The payload records a change flag, never the secret itself.
        recorded.Payload.Should().ContainKey("ClientSecret");
        recorded.Payload!["ClientSecret"].Should().Be("set");
        recorded.Payload.Values.Should().NotContain("brand-new-secret");
    }

    [Fact]
    public async Task OnGet_LoadsSections_WithSourceBadgesAndEffectiveValues()
    {
        var (page, store, _, _) = BuildPage();
        store.Setup(s => s.ListAsync("Entra", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new() { Key = "TenantId", IsSecret = false, HasValue = true, Value = "db-tenant" },
                new() { Key = "ClientSecret", IsSecret = true, HasValue = true, Value = null },
            ]);

        await page.OnGetAsync(CancellationToken.None);

        page.ConfigAvailable.Should().BeTrue();
        var section = page.Sections.Should().ContainSingle().Subject;
        section.Adapter.Should().Be("Entra");

        var tenant = section.Fields.Single(f => f.Field.Key == "TenantId");
        tenant.HasDbValue.Should().BeTrue();
        tenant.EffectiveValue.Should().Be("db-tenant");

        var secret = section.Fields.Single(f => f.Field.Key == "ClientSecret");
        secret.HasDbValue.Should().BeTrue();
        secret.DbValue.Should().BeNull("a secret's plaintext never reaches the view");
    }

    [Fact]
    public async Task OnPostSave_UnknownAdapter_SurfacesError_AndDoesNotNotify()
    {
        var (page, _, _, notifier) = BuildPage();

        await page.OnPostSaveAsync("DoesNotExist", CancellationToken.None);

        page.Errors.Should().NotBeEmpty();
        notifier.Fired.Should().BeFalse();
    }

    [Fact]
    public async Task OnPostSave_StoreFailure_RecordsErrors_AuditsFailure_AndDoesNotNotify()
    {
        var (page, store, audit, notifier) = BuildPage();
        store.Setup(s => s.SaveAsync(It.IsAny<SaveAdapterConfigCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserResult.Failure("write blocked"));
        AuditEvent? recorded = null;
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEvent, CancellationToken>((e, _) => recorded = e)
            .Returns(Task.CompletedTask);

        page.FieldValues["TenantId"] = "tenant-x";
        await page.OnPostSaveAsync(Adapter, CancellationToken.None);

        page.Errors.Should().Contain("write blocked");
        notifier.Fired.Should().BeFalse("a failed save must not trigger a reload");
        recorded.Should().NotBeNull();
        recorded!.Outcome.Should().Be(AuditOutcome.Failure);
    }

    [Fact]
    public async Task OnPostSave_NoStore_IsNoOp()
    {
        var page = new IndexModel(
            BuildLocalizer().Object,
            new Mock<IAuditWriter>().Object,
            [new FakeSchema()],
            [],
            store: null);

        var result = await page.OnPostSaveAsync(Adapter, CancellationToken.None);

        result.Should().NotBeNull();
        page.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ConfigAvailable_IsFalse_WhenNoStoreRegistered()
    {
        var page = new IndexModel(
            BuildLocalizer().Object,
            new Mock<IAuditWriter>().Object,
            [new FakeSchema()],
            [],
            store: null);

        page.ConfigAvailable.Should().BeFalse();
    }

    private static (IndexModel Page, Mock<IAdapterConfigStore> Store, Mock<IAuditWriter> Audit, FakeNotifier Notifier) BuildPage()
    {
        var store = new Mock<IAdapterConfigStore>();
        store.Setup(s => s.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var audit = new Mock<IAuditWriter>();
        var notifier = new FakeNotifier();

        var page = new IndexModel(
            BuildLocalizer().Object,
            audit.Object,
            [new FakeSchema()],
            [notifier],
            store.Object);

        return (page, store, audit, notifier);
    }

    private static Mock<IStringLocalizer<AdminSharedResources>> BuildLocalizer()
    {
        var localizer = new Mock<IStringLocalizer<AdminSharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));
        localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns((string key, object[] _) => new LocalizedString(key, key));
        return localizer;
    }

    private sealed class FakeSchema : IAdapterConfigSchema
    {
        public string Adapter => "Entra";
        public string DisplayName => "Microsoft Entra ID";
        public IReadOnlyList<AdapterConfigField> Fields { get; } =
        [
            new() { Key = "TenantId", Label = "Tenant ID", IsRequired = true },
            new() { Key = "ClientSecret", Label = "Client secret", IsSecret = true, IsRequired = true },
        ];
        public bool HasCodeValue(string key) => false;
        public string? GetCodeValue(string key) => null;
    }

    private sealed class FakeNotifier : IAdapterConfigChangeNotifier
    {
        public string Adapter => "Entra";
        public bool Fired { get; private set; }
        public void NotifyChanged() => Fired = true;
    }
}
