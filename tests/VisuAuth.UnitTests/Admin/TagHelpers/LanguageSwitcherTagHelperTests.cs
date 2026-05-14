using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.AdminUi;
using VisuAuth.AdminUi.Localization;
using VisuAuth.AdminUi.TagHelpers;
using Xunit;

namespace VisuAuth.UnitTests.Admin.TagHelpers;

/// <summary>
/// Covers the suppression paths of <see cref="LanguageSwitcherTagHelper"/> —
/// the happy-path render is exercised end-to-end by the integration tests
/// at <c>LocalizationTests.GetAdmin_SidebarRendersLanguageSwitcher</c> etc.
/// </summary>
public sealed class LanguageSwitcherTagHelperTests
{
    [Fact]
    public void Process_WithNoHttpContext_SuppressesOutput()
    {
        // Razor compilation can render tag helpers from a non-request
        // context (design-time / pre-render); in those cases there is
        // nothing meaningful to post to so the helper must stay silent.
        var helper = MakeHelper(httpContext: null, supportedCultures: ["en", "pt-BR"]);

        var output = RenderToOutput(helper);

        output.TagName.Should().BeNull("SuppressOutput clears the tag name");
    }

    [Fact]
    public void Process_WithSingleSupportedCulture_SuppressesOutput()
    {
        // No point showing a one-option dropdown — and emitting one would
        // confuse screen-reader users who expect choice when a switcher is
        // present. Hide the entire form when the consumer has only one
        // culture configured.
        var helper = MakeHelper(httpContext: new DefaultHttpContext(), supportedCultures: ["en"]);

        var output = RenderToOutput(helper);

        output.TagName.Should().BeNull();
    }

    [Fact]
    public void Process_WithZeroSupportedCultures_SuppressesOutput()
    {
        // SupportedUICultures should never realistically be empty (the
        // pipeline auto-seeds "en" when nothing is configured), but the
        // helper still guards against it.
        var helper = MakeHelper(httpContext: new DefaultHttpContext(), supportedCultures: []);

        var output = RenderToOutput(helper);

        output.TagName.Should().BeNull();
    }

    private static LanguageSwitcherTagHelper MakeHelper(
        HttpContext? httpContext,
        string[] supportedCultures)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var antiforgery = new Mock<IAntiforgery>();
        // Not exercised in suppression paths — but keep the mock loose.

        var requestOptions = Options.Create(new RequestLocalizationOptions
        {
            SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList(),
        });
        var vauOptions = Options.Create(new VisuAuthLocalizationOptions());

        var localizer = new Mock<IStringLocalizer<AdminSharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));

        return new LanguageSwitcherTagHelper(
            accessor.Object,
            antiforgery.Object,
            requestOptions,
            vauOptions,
            localizer.Object);
    }

    private static TagHelperOutput RenderToOutput(LanguageSwitcherTagHelper helper)
    {
        var context = new TagHelperContext(
            tagName: "va-language-switcher",
            allAttributes: [],
            items: new Dictionary<object, object>(),
            uniqueId: "test");
        var output = new TagHelperOutput(
            "va-language-switcher",
            [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(context, output);
        return output;
    }
}
