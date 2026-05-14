using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.AdminUi.Localization;
using Xunit;

namespace VisuAuth.UnitTests.Admin.Localization;

/// <summary>
/// Covers the request-localization configuration produced by
/// <see cref="VisuAuthLocalizationExtensions.AddVisuAuthLocalization"/>.
/// The integration suite exercises the happy path end to end; these
/// tests pin the edge cases without booting a host.
/// </summary>
public sealed class VisuAuthLocalizationExtensionsTests
{
    [Fact]
    public void AddVisuAuthLocalization_WithDefaults_EnglishIsTheDefaultCulture()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddVisuAuthLocalization()
            .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        options.DefaultRequestCulture.UICulture.Name.Should().Be("en",
            "the first SupportedCulture is the default — and the package ships 'en' as the first entry");
        options.SupportedUICultures!.Select(c => c.Name).Should().BeEquivalentTo("en", "pt-BR");
    }

    [Fact]
    public void AddVisuAuthLocalization_WhenConsumerEmptiesSupportedCultures_FallsBackToEnglish()
    {
        // Defensive path: a consumer could call configure() and Clear()
        // the list. The Configure delegate fills back with "en" so the
        // pipeline never tries to register a culture-less options bag.
        var sp = new ServiceCollection()
            .AddLogging()
            .AddVisuAuthLocalization(vau => vau.SupportedCultures.Clear())
            .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        options.SupportedUICultures.Should().NotBeNull();
        options.SupportedUICultures!.Single().Name.Should().Be("en");
        options.DefaultRequestCulture.UICulture.Name.Should().Be("en");
    }

    [Fact]
    public void AddVisuAuthLocalization_PinsThreeRequestCultureProviders_InOrder()
    {
        // Source-of-truth check: query first (so a deep link wins), cookie
        // next (sticks after switcher use), Accept-Language last (browser
        // default). Re-ordering would silently change behaviour for every
        // consumer — pinning the order here surfaces the regression.
        var sp = new ServiceCollection()
            .AddLogging()
            .AddVisuAuthLocalization()
            .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        options.RequestCultureProviders.Should().SatisfyRespectively(
            p => p.Should().BeOfType<QueryStringRequestCultureProvider>(),
            p => p.Should().BeOfType<CookieRequestCultureProvider>(),
            p => p.Should().BeOfType<AcceptLanguageHeaderRequestCultureProvider>());
    }

    [Fact]
    public void AddVisuAuthLocalization_WithCustomConfigure_AppliesOverrides()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddVisuAuthLocalization(vau =>
            {
                vau.CookieName = "my-app-culture";
                vau.FormFieldName = "lang";
                vau.SupportedCultures.Add(new CultureInfo("es"));
            })
            .BuildServiceProvider();

        var vau = sp.GetRequiredService<IOptions<VisuAuthLocalizationOptions>>().Value;
        var req = sp.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        vau.CookieName.Should().Be("my-app-culture");
        vau.FormFieldName.Should().Be("lang");
        req.SupportedUICultures!.Select(c => c.Name).Should().Contain("es");
    }
}
