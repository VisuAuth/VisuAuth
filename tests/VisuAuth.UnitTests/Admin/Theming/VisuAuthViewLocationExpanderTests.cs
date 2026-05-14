using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.UnitTests.Admin.Theming;

/// <summary>
/// Locks down the path-prepend logic that powers theming layer 3
/// (CLAUDE.md §8.4). The integration tests exercise the happy path
/// end-to-end; these unit tests target the branches a single
/// integration scenario can't reach: empty/whitespace roots, the
/// IOptionsMonitor live re-read, and every <c>Normalize</c> shape.
/// </summary>
public sealed class VisuAuthViewLocationExpanderTests
{
    private const string CacheKey = "visuauth-view-override-root";

    private static readonly string[] BaseLocations =
    [
        "/Pages/{0}.cshtml",
        "/Pages/Shared/{0}.cshtml",
    ];

    private static readonly string[] ExpectedDefaultPrefix =
    [
        "/Views/VisuAuth/{0}.cshtml",
        "/Views/VisuAuth/Shared/{0}.cshtml",
    ];

    private static readonly string[] ExpectedTenantThenGlobalPrefix =
    [
        "/Views/VisuAuth/Tenants/acme/{0}.cshtml",
        "/Views/VisuAuth/Tenants/acme/Shared/{0}.cshtml",
        "/Views/VisuAuth/{0}.cshtml",
        "/Views/VisuAuth/Shared/{0}.cshtml",
    ];

    private static readonly string[] ExpectedTenantOnlyPrefix =
    [
        "/Views/VisuAuth/Tenants/acme/{0}.cshtml",
        "/Views/VisuAuth/Tenants/acme/Shared/{0}.cshtml",
    ];

    [Fact]
    public void ExpandViewLocations_WithDefaultRoot_PrependsTwoOverrideSlots()
    {
        var expander = CreateExpander("/Views/VisuAuth");
        var context = CreateContext();
        expander.PopulateValues(context);

        var result = expander.ExpandViewLocations(context, BaseLocations).ToArray();

        result.Should().StartWith(ExpectedDefaultPrefix);
        // Original locations must follow — the override is additive, not destructive.
        result.Should().EndWith(BaseLocations);
        result.Should().HaveCount(BaseLocations.Length + 2);
    }

    [Fact]
    public void ExpandViewLocations_WithEmptyRoot_LeavesViewLocationsUntouched()
    {
        // Defensive guard: a consumer that explicitly clears the root
        // (e.g. to disable overrides without removing the registration)
        // must not get a phantom "//{0}.cshtml" entry.
        var expander = CreateExpander(string.Empty);
        var context = CreateContext();
        expander.PopulateValues(context);

        var result = expander.ExpandViewLocations(context, BaseLocations);

        result.Should().BeSameAs(BaseLocations,
            "an empty root should short-circuit and return the input unchanged");
    }

    [Fact]
    public void ExpandViewLocations_WithoutPopulatedCacheKey_LeavesViewLocationsUntouched()
    {
        // PopulateValues runs first under normal Razor flow — but if a
        // future Razor change skipped it, the expander must still degrade
        // gracefully instead of throwing on a missing key.
        var expander = CreateExpander("/Views/VisuAuth");
        var context = CreateContext();
        // intentionally skipping PopulateValues

        var result = expander.ExpandViewLocations(context, BaseLocations);

        result.Should().BeSameAs(BaseLocations);
    }

    [Theory]
    [InlineData("/Views/VisuAuth", "/Views/VisuAuth")]
    [InlineData("/Views/VisuAuth/", "/Views/VisuAuth", "trailing slash collapses to avoid '//{0}.cshtml'")]
    [InlineData("Views/VisuAuth", "/Views/VisuAuth", "missing leading slash gets one prepended")]
    [InlineData("\\Views\\VisuAuth", "/Views/VisuAuth", "Windows-style backslashes flip to forward slashes")]
    [InlineData("Views/VisuAuth\\Shared/", "/Views/VisuAuth/Shared", "mixed separators + trailing slash both normalised")]
    public void PopulateValues_NormalisesRootBeforeStashingInCacheKey(
        string configured, string expected, string? reason = null)
    {
        var expander = CreateExpander(configured);
        var context = CreateContext();

        expander.PopulateValues(context);

        context.Values[CacheKey].Should().Be(expected, reason ?? "");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PopulateValues_WithBlankOrNullRoot_StashesEmptyString(string? root)
    {
        var expander = CreateExpander(root!);
        var context = CreateContext();

        expander.PopulateValues(context);

        context.Values[CacheKey].Should().Be(string.Empty,
            "the cache key must still be set so Razor's cache key changes when overrides toggle on/off");
    }

    [Fact]
    public void PopulateValues_OnEachCall_RereadsCurrentValueFromOptionsMonitor()
    {
        // Live re-read is the whole point of going through IOptionsMonitor:
        // a consumer that reconfigures the root at runtime (or via reload)
        // must take effect on the very next request.
        var monitor = new MutableMonitor("/Views/First");
        var expander = new VisuAuthViewLocationExpander(monitor);
        var context = CreateContext();

        expander.PopulateValues(context);
        context.Values[CacheKey].Should().Be("/Views/First");

        monitor.Set("/Views/Second");
        expander.PopulateValues(context);
        context.Values[CacheKey].Should().Be("/Views/Second");
    }

    [Fact]
    public void PopulateValues_WithNullContext_Throws()
    {
        var expander = CreateExpander("/x");
        var act = () => expander.PopulateValues(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExpandViewLocations_WithNullContext_Throws()
    {
        var expander = CreateExpander("/x");
        var act = () => expander.ExpandViewLocations(null!, BaseLocations);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExpandViewLocations_WithNullViewLocations_Throws()
    {
        var expander = CreateExpander("/x");
        var act = () => expander.ExpandViewLocations(CreateContext(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---------- Per-tenant override slot (layer 3 + 4 composed) ----------

    [Fact]
    public void ExpandViewLocations_WithTenantRootResolved_PrependsTenantSlotAheadOfGlobal()
    {
        // Order matters: tenant override → global override → defaults.
        // The first match wins, so a per-tenant _UsersTable.cshtml beats
        // both the consumer-wide override and the package default.
        var expander = CreateExpander(globalRoot: "/Views/VisuAuth");
        var context = CreateContextWithTenant(currentTenantId: "acme",
            tenantRoot: "/Views/VisuAuth/Tenants/acme");

        expander.PopulateValues(context);
        var result = expander.ExpandViewLocations(context, BaseLocations).ToArray();

        result.Should().StartWith(ExpectedTenantThenGlobalPrefix);
        result.Should().EndWith(BaseLocations);
        result.Should().HaveCount(BaseLocations.Length + 4);
    }

    [Fact]
    public void ExpandViewLocations_WithTenantRootResolvedAndNoGlobal_StillPrependsTenantSlot()
    {
        // Per-tenant override is independent of the global slot — a
        // consumer who uses ONLY per-tenant overrides (no /Views/VisuAuth/)
        // must still see their override picked up.
        var expander = CreateExpander(globalRoot: string.Empty);
        var context = CreateContextWithTenant(currentTenantId: "acme",
            tenantRoot: "/Views/VisuAuth/Tenants/acme");

        expander.PopulateValues(context);
        var result = expander.ExpandViewLocations(context, BaseLocations).ToArray();

        result.Should().StartWith(ExpectedTenantOnlyPrefix);
        result.Should().EndWith(BaseLocations);
        result.Should().HaveCount(BaseLocations.Length + 2);
    }

    [Fact]
    public void ExpandViewLocations_WhenResolverReturnsNull_FallsBackToGlobalOnly()
    {
        // The resolver returning null is the "no per-tenant override"
        // signal — must drop straight into the layer-3 behaviour.
        var expander = CreateExpander(globalRoot: "/Views/VisuAuth");
        var context = CreateContextWithTenant(currentTenantId: "acme",
            tenantRoot: null);

        expander.PopulateValues(context);
        var result = expander.ExpandViewLocations(context, BaseLocations).ToArray();

        result.Should().StartWith(ExpectedDefaultPrefix);
        result.Should().HaveCount(BaseLocations.Length + 2,
            "no tenant root resolved → only the global slot is prepended");
    }

    [Fact]
    public void PopulateValues_StashesNormalisedTenantRootInItsOwnCacheKey()
    {
        // The cache key must include the resolved tenant id so Razor
        // doesn't serve tenant A's swapped _UsersTable to tenant B on
        // the next request from a recycled cache entry.
        var expander = CreateExpander(globalRoot: "/Views/VisuAuth");
        var context = CreateContextWithTenant(currentTenantId: "acme",
            tenantRoot: "Views\\VisuAuth\\Tenants\\acme/");

        expander.PopulateValues(context);

        context.Values["visuauth-view-override-root"]
            .Should().Be("/Views/VisuAuth", "global root keeps its own cache slot");
        context.Values["visuauth-view-override-tenant-root"]
            .Should().Be("/Views/VisuAuth/Tenants/acme",
                "tenant root cache key gets the same Normalize() treatment as the global root");
    }

    [Fact]
    public void PopulateValues_WhenMultiTenancyDisabled_LeavesTenantSlotEmpty()
    {
        // Single-tenant deployments (tenantContext.IsMultiTenancyEnabled
        // == false) must short-circuit — even if a resolver is registered,
        // the expander never asks it. Locks in the no-cost fast path.
        var expander = CreateExpander(globalRoot: "/Views/VisuAuth");
        var context = CreateContextWithTenant(
            currentTenantId: "ignored-because-multi-tenancy-is-off",
            tenantRoot: "/should-never-be-prepended",
            multiTenancyEnabled: false);

        expander.PopulateValues(context);

        context.Values["visuauth-view-override-tenant-root"].Should().Be(string.Empty);
    }

    [Fact]
    public void PopulateValues_WithoutHttpContextRequestServices_DegradesToGlobalOnly()
    {
        // ResolveTenantRoot defends against null RequestServices (test
        // harnesses, framework edge cases). Without that guard the
        // expander would throw and crash every render.
        var expander = CreateExpander(globalRoot: "/Views/VisuAuth");
        var context = CreateContext();   // no RequestServices wired

        expander.PopulateValues(context);

        context.Values["visuauth-view-override-tenant-root"].Should().Be(string.Empty);
    }

    [Fact]
    public void PopulateValues_WithRequestServicesButNoTenantContext_DegradesToGlobalOnly()
    {
        // Defensive: a host that wires RequestServices but doesn't
        // register ITenantContext (e.g. a consumer who skipped the
        // VisuAuth Identity adapter) must still render — the expander
        // should treat "no tenant context" as "no per-tenant override".
        var expander = CreateExpander(globalRoot: "/Views/VisuAuth");

        // Empty service provider — neither ITenantContext nor the
        // resolver are registered.
        var emptyServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = emptyServices };
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            http,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var context = new ViewLocationExpanderContext(
            actionContext,
            viewName: "_x",
            controllerName: null,
            areaName: null,
            pageName: null,
            isMainPage: false)
        {
            Values = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        expander.PopulateValues(context);

        context.Values["visuauth-view-override-tenant-root"].Should().Be(string.Empty);
    }

    [Fact]
    public void PopulateValues_WithTenantContextButNoResolverRegistered_DegradesToGlobalOnly()
    {
        // Multi-tenancy on, but the consumer didn't register an
        // ITenantViewOverrideResolver — the AdminUi DI extension's
        // TryAdd default would normally win, but in a test where the
        // RequestServices is hand-built without it we must still degrade
        // gracefully instead of throwing on a null resolver.
        var expander = CreateExpander(globalRoot: "/Views/VisuAuth");

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(c => c.IsMultiTenancyEnabled).Returns(true);
        tenantContext.SetupGet(c => c.CurrentTenantId).Returns("acme");

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(tenantContext.Object);
        // Intentionally NOT registering ITenantViewOverrideResolver.
        var provider = services.BuildServiceProvider();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = provider };
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            http,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var context = new ViewLocationExpanderContext(
            actionContext,
            viewName: "_x",
            controllerName: null,
            areaName: null,
            pageName: null,
            isMainPage: false)
        {
            Values = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        expander.PopulateValues(context);

        context.Values["visuauth-view-override-tenant-root"].Should().Be(string.Empty);
    }

    private static VisuAuthViewLocationExpander CreateExpander(string globalRoot)
    {
        var options = new VisuAuthViewOverrideOptions { Root = globalRoot };
        var monitor = new Mock<IOptionsMonitor<VisuAuthViewOverrideOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new VisuAuthViewLocationExpander(monitor.Object);
    }

    private static ViewLocationExpanderContext CreateContext()
    {
        // ViewLocationExpanderContext is constructed by Razor at runtime;
        // we don't need a fully-wired one for these tests — the expander
        // only ever touches Values + HttpContext.RequestServices.
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var ctx = new ViewLocationExpanderContext(
            actionContext,
            viewName: "_UsersTable",
            controllerName: null,
            areaName: null,
            pageName: null,
            isMainPage: false)
        {
            Values = new Dictionary<string, string?>(StringComparer.Ordinal),
        };
        return ctx;
    }

    /// <summary>
    /// Builds a <see cref="ViewLocationExpanderContext"/> whose
    /// <c>HttpContext.RequestServices</c> resolves both
    /// <see cref="ITenantContext"/> and
    /// <see cref="ITenantViewOverrideResolver"/> — the two scoped
    /// services the expander pulls per-request to produce the
    /// per-tenant override root.
    /// </summary>
    private static ViewLocationExpanderContext CreateContextWithTenant(
        string? currentTenantId,
        string? tenantRoot,
        bool multiTenancyEnabled = true)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(c => c.IsMultiTenancyEnabled).Returns(multiTenancyEnabled);
        tenantContext.SetupGet(c => c.CurrentTenantId).Returns(currentTenantId);

        var resolver = new Mock<ITenantViewOverrideResolver>();
        resolver.Setup(r => r.ResolveOverrideRoot(It.IsAny<string?>())).Returns(tenantRoot);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(tenantContext.Object);
        services.AddSingleton(resolver.Object);
        var provider = services.BuildServiceProvider();

        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = provider };
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            http,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        return new ViewLocationExpanderContext(
            actionContext,
            viewName: "_UsersTable",
            controllerName: null,
            areaName: null,
            pageName: null,
            isMainPage: false)
        {
            Values = new Dictionary<string, string?>(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Tiny IOptionsMonitor stub that lets the test mutate CurrentValue
    /// after construction to prove the expander reads it on every call.
    /// </summary>
    private sealed class MutableMonitor(string root) : IOptionsMonitor<VisuAuthViewOverrideOptions>
    {
        private VisuAuthViewOverrideOptions _value = new() { Root = root };

        public VisuAuthViewOverrideOptions CurrentValue => _value;

        public VisuAuthViewOverrideOptions Get(string? name) => _value;

        public IDisposable? OnChange(Action<VisuAuthViewOverrideOptions, string?> listener) => null;

        public void Set(string root) => _value = new VisuAuthViewOverrideOptions { Root = root };
    }
}
