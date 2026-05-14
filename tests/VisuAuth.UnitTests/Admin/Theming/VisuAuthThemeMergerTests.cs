using FluentAssertions;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.UnitTests.Admin.Theming;

/// <summary>
/// Locks down the property-by-property overlay rule that powers
/// theming layer 4 (CLAUDE.md §8.4). Tenant overrides win where set,
/// the global theme fills the rest, and anything still null falls
/// through to the CSS defaults declared in <c>visuauth.css</c>.
/// </summary>
public sealed class VisuAuthThemeMergerTests
{
    [Fact]
    public void Merge_WithNullOverrides_ReturnsCopyOfBase()
    {
        var @base = new VisuAuthTheme { Primary = "#fff", Bg = "#000" };

        var merged = VisuAuthThemeMerger.Merge(@base, null);

        merged.Should().NotBeSameAs(@base, "callers must not be able to mutate the source theme");
        merged.Primary.Should().Be("#fff");
        merged.Bg.Should().Be("#000");
    }

    [Fact]
    public void Merge_WithEmptyOverrides_ReturnsBaseValues()
    {
        var @base = new VisuAuthTheme { Primary = "#fff", Bg = "#000" };
        var overrides = new VisuAuthTheme();

        var merged = VisuAuthThemeMerger.Merge(@base, overrides);

        merged.Primary.Should().Be("#fff");
        merged.Bg.Should().Be("#000");
    }

    [Fact]
    public void Merge_WithSingleOverrideProperty_LeavesOtherBaseValuesIntact()
    {
        // The whole point of the merge: a tenant resolver can override
        // one colour without restating the rest of the palette.
        var @base = new VisuAuthTheme { Primary = "#blue", PrimaryFg = "#white", Bg = "#fff" };
        var overrides = new VisuAuthTheme { Primary = "#red" };

        var merged = VisuAuthThemeMerger.Merge(@base, overrides);

        merged.Primary.Should().Be("#red", "tenant override wins");
        merged.PrimaryFg.Should().Be("#white", "global theme fills properties the tenant left null");
        merged.Bg.Should().Be("#fff");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void Merge_WithBlankOverrideProperty_FallsThroughToBase(string? override_)
    {
        // Whitespace counts as "unset" — same rule the CSS renderer
        // applies, so a tenant resolver returning Primary = " " behaves
        // like Primary = null (the global theme keeps showing through).
        var @base = new VisuAuthTheme { Primary = "#blue" };
        var overrides = new VisuAuthTheme { Primary = override_ };

        var merged = VisuAuthThemeMerger.Merge(@base, overrides);

        merged.Primary.Should().Be("#blue");
    }

    [Fact]
    public void Merge_AllProperties_OverlaysIndependently()
    {
        // Belt-and-suspenders: walk every VisuAuthTheme property to
        // catch a missed branch in the merger if a new property is
        // added to the theme bag without updating the merger.
        var @base = new VisuAuthTheme
        {
            Primary = "b1",
            PrimaryFg = "b2",
            Bg = "b3",
            Fg = "b4",
            Muted = "b5",
            Border = "b6",
            Surface = "b7",
            Danger = "b8",
            Success = "b9",
            Radius = "b10",
            Font = "b11",
        };
        var overrides = new VisuAuthTheme
        {
            Primary = "o1",
            PrimaryFg = "o2",
            Bg = "o3",
            Fg = "o4",
            Muted = "o5",
            Border = "o6",
            Surface = "o7",
            Danger = "o8",
            Success = "o9",
            Radius = "o10",
            Font = "o11",
        };

        var merged = VisuAuthThemeMerger.Merge(@base, overrides);

        merged.Primary.Should().Be("o1");
        merged.PrimaryFg.Should().Be("o2");
        merged.Bg.Should().Be("o3");
        merged.Fg.Should().Be("o4");
        merged.Muted.Should().Be("o5");
        merged.Border.Should().Be("o6");
        merged.Surface.Should().Be("o7");
        merged.Danger.Should().Be("o8");
        merged.Success.Should().Be("o9");
        merged.Radius.Should().Be("o10");
        merged.Font.Should().Be("o11");
    }

    [Fact]
    public void Merge_WithBothEmpty_ReturnsEmptyTheme()
    {
        var merged = VisuAuthThemeMerger.Merge(new VisuAuthTheme(), new VisuAuthTheme());

        merged.Primary.Should().BeNull();
        merged.Bg.Should().BeNull();
    }

    [Fact]
    public void Merge_DoesNotMutateInputs()
    {
        // Defensive: callers may keep references to the global theme.
        // Any mutation here would corrupt every subsequent render.
        var @base = new VisuAuthTheme { Primary = "#blue" };
        var overrides = new VisuAuthTheme { Bg = "#000" };

        _ = VisuAuthThemeMerger.Merge(@base, overrides);

        @base.Primary.Should().Be("#blue");
        @base.Bg.Should().BeNull();
        overrides.Primary.Should().BeNull();
        overrides.Bg.Should().Be("#000");
    }

    [Fact]
    public void Merge_WithNullBase_Throws()
    {
        var act = () => VisuAuthThemeMerger.Merge(null!, new VisuAuthTheme());

        act.Should().Throw<ArgumentNullException>();
    }
}
