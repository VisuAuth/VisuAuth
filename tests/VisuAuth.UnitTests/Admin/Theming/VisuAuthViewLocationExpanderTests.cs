using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Options;
using Moq;
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

    private static VisuAuthViewLocationExpander CreateExpander(string root)
    {
        var options = new VisuAuthViewOverrideOptions { Root = root };
        var monitor = new Mock<IOptionsMonitor<VisuAuthViewOverrideOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new VisuAuthViewLocationExpander(monitor.Object);
    }

    private static ViewLocationExpanderContext CreateContext()
    {
        // ViewLocationExpanderContext is constructed by Razor at runtime;
        // we don't need a fully-wired one for these tests — the expander
        // only ever touches Values.
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
