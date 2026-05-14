using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Razor.Hosting;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.UnitTests.Admin.Theming;

/// <summary>
/// Unit-level checks for the page-demotion half of theming layer 3
/// (CLAUDE.md §8.4). The integration test asserts the convention is
/// registered; these tests cover its <c>Apply</c> branches in isolation
/// — wrong assembly, missing RazorCompiledItem, attribute-route-less
/// selector — so a regression in the early-return logic doesn't slip
/// past coverage.
/// </summary>
public sealed class DemoteVisuAuthPagesConventionTests
{
    private static readonly Assembly OurAssembly = typeof(DemoteVisuAuthPagesConventionTests).Assembly;
    private static readonly Assembly OtherAssembly = typeof(string).Assembly;

    [Fact]
    public void OwnsAssembly_WithMatchingAssembly_ReturnsTrue()
    {
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);

        convention.OwnsAssembly(OurAssembly).Should().BeTrue();
    }

    [Fact]
    public void OwnsAssembly_WithDifferentAssembly_ReturnsFalse()
    {
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);

        convention.OwnsAssembly(OtherAssembly).Should().BeFalse(
            "the per-assembly guard is what stops a duplicate registration from stacking conventions");
    }

    [Fact]
    public void Apply_WithRazorItemFromOurAssembly_DemotesEverySelectorWithAttributeRoute()
    {
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);
        var model = CreateModel(itemAssembly: OurAssembly,
            new SelectorModel { AttributeRouteModel = new AttributeRouteModel { Template = "visuauth/login", Order = 0 } },
            new SelectorModel { AttributeRouteModel = new AttributeRouteModel { Template = "visuauth/login/{id}", Order = 0 } });

        convention.Apply(model);

        model.Selectors.Should().AllSatisfy(s =>
            s.AttributeRouteModel!.Order.Should().Be(DemoteVisuAuthPagesConvention.OverridableOrder));
    }

    [Fact]
    public void Apply_WithRazorItemFromOtherAssembly_LeavesOrderUntouched()
    {
        // The whole point of the assembly check: a consumer page that
        // happens to have the same RelativePath as one of ours must not
        // get its route order bumped.
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);
        var model = CreateModel(itemAssembly: OtherAssembly,
            new SelectorModel { AttributeRouteModel = new AttributeRouteModel { Template = "visuauth/login", Order = 0 } });

        convention.Apply(model);

        model.Selectors[0].AttributeRouteModel!.Order.Should().Be(0);
    }

    [Fact]
    public void Apply_WithMissingRazorCompiledItem_LeavesOrderUntouched()
    {
        // PageRouteModels from non-Razor sources (e.g. AddPageRoute in
        // RazorPagesOptions) won't have the property — must short-circuit
        // instead of throwing on the missing key.
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);
        var model = new PageRouteModel(relativePath: "/Pages/Login.cshtml", viewEnginePath: "/Login");
        model.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel { Template = "visuauth/login", Order = 0 },
        });

        convention.Apply(model);

        model.Selectors[0].AttributeRouteModel!.Order.Should().Be(0);
    }

    [Fact]
    public void Apply_WithPropertyHoldingNonRazorItemValue_LeavesOrderUntouched()
    {
        // Defensive: someone (or a future ASP.NET version) puts something
        // other than a RazorCompiledItem under the same key — the
        // type-guarded pattern must reject it instead of throwing.
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);
        var model = new PageRouteModel(relativePath: "/Pages/Login.cshtml", viewEnginePath: "/Login");
        model.Properties[typeof(RazorCompiledItem)] = "this is not a RazorCompiledItem";
        model.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel { Template = "visuauth/login", Order = 0 },
        });

        convention.Apply(model);

        model.Selectors[0].AttributeRouteModel!.Order.Should().Be(0);
    }

    [Fact]
    public void Apply_WithSelectorMissingAttributeRouteModel_SkipsThatSelector()
    {
        // Conventional-routed pages (no @page) have no attribute route to
        // demote. Loop must skip them without exploding.
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);
        var model = CreateModel(itemAssembly: OurAssembly,
            new SelectorModel { AttributeRouteModel = null },
            new SelectorModel { AttributeRouteModel = new AttributeRouteModel { Template = "visuauth/login", Order = 0 } });

        convention.Apply(model);

        model.Selectors[0].AttributeRouteModel.Should().BeNull();
        model.Selectors[1].AttributeRouteModel!.Order.Should().Be(DemoteVisuAuthPagesConvention.OverridableOrder);
    }

    [Fact]
    public void Apply_WithNullModel_Throws()
    {
        var convention = new DemoteVisuAuthPagesConvention(OurAssembly);

        var act = () => convention.Apply(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OverridableOrder_IsThePinnedSentinel()
    {
        // Locked at 1000 so production code, tests, and docs stay in
        // sync. Anything > 0 (the default for consumer pages) works for
        // routing — but the value is asserted in the integration test as
        // documentation, so changing it here is a breaking signal.
        DemoteVisuAuthPagesConvention.OverridableOrder.Should().Be(1000);
    }

    private static PageRouteModel CreateModel(Assembly itemAssembly, params SelectorModel[] selectors)
    {
        var model = new PageRouteModel(
            relativePath: "/Pages/Login.cshtml",
            viewEnginePath: "/Login");
        model.Properties[typeof(RazorCompiledItem)] = new FakeRazorCompiledItem(itemAssembly);
        foreach (var selector in selectors)
        {
            model.Selectors.Add(selector);
        }
        return model;
    }

    /// <summary>
    /// Minimal RazorCompiledItem stand-in: only its <see cref="Type"/>
    /// is read by the convention, and only the type's assembly matters.
    /// </summary>
    private sealed class FakeRazorCompiledItem(Assembly assembly) : RazorCompiledItem
    {
        // The convention reads Type.Assembly, so we hand it any type
        // whose Assembly is the assembly we want to fake.
        public override Type Type { get; } = assembly == typeof(string).Assembly
            ? typeof(string)
            : typeof(FakeRazorCompiledItem);

        public override string Identifier => "/Pages/Login.cshtml";
        public override string Kind => "mvc.1.0.razor-page";
        public override IReadOnlyList<object> Metadata => Array.Empty<object>();
    }
}
