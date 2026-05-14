using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.Localization;

/// <summary>
/// End-to-end coverage of the JSON-backed localization pipeline: query
/// string, cookie, and Accept-Language all resolve to the right culture,
/// the inline language switcher round-trips, and unknown cultures fall
/// back instead of crashing.
/// </summary>
public sealed partial class LocalizationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    [Fact]
    public async Task GetLogin_WithoutCulturePreference_RendersEnglishByDefault()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain(">Sign in</h1>",
            "English heading must render with the default culture");
        body.Should().Contain("Forgot password?", "English helper link must render");
        body.Should().NotContain(">Entrar</h1>", "Portuguese should not leak into the default culture");
    }

    [Fact]
    public async Task GetLogin_WithCultureQuery_RendersPortuguese()
    {
        // QueryStringRequestCultureProvider is first in the chain so this
        // overrides any other signal.
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/login?culture=pt-BR", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain(">Entrar</h1>", "pt-BR heading must override the default");
        body.Should().Contain("Esqueceu a senha?",
            "pt-BR helper link must render — confirms JSON pt-BR file is being read");
    }

    [Fact]
    public async Task GetLogin_WithAcceptLanguageHeader_RendersPortuguese()
    {
        // Accept-Language is last in the provider chain, so cookie / query
        // would override it. Without those, the header still wins over the
        // default.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");

        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain(">Entrar</h1>");
    }

    [Fact]
    public async Task GetAdmin_LayoutCarriesLangAttribute_MatchingCurrentCulture()
    {
        using var client = factory.CreateClient();

        var en = await (await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative)))
            .Content.ReadAsStringAsync();
        var pt = await (await client.GetAsync(new Uri("/visuauth/admin/users?culture=pt-BR", UriKind.Relative)))
            .Content.ReadAsStringAsync();

        en.Should().Contain("<html lang=\"en\">",
            "the layout must reflect the current UI culture so screen readers pick the right voice");
        pt.Should().Contain("<html lang=\"pt-BR\">");
    }

    [Fact]
    public async Task PostCulture_WithSupportedCulture_PersistsCookieAndRedirects()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        // The endpoint requires antiforgery — grab one from any page.
        var seed = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var token = ExtractToken(await seed.Content.ReadAsStringAsync());

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["culture"] = "pt-BR",
            ["returnUrl"] = "/visuauth/login",
        });

        var response = await client.PostAsync(new Uri("/visuauth/culture", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.Should().Be("/visuauth/login");

        // The Set-Cookie header must carry the canonical CookieRequestCultureProvider
        // value (which is URL-encoded and starts with "c=" / "uic=").
        var setCookie = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith(".AspNetCore.Culture=", StringComparison.OrdinalIgnoreCase));
        setCookie.Should().NotBeNull("the endpoint must write the locale cookie");
        setCookie.Should().Contain("pt-BR");
    }

    [Fact]
    public async Task PostCulture_ThenGetLogin_HonoursPersistedCookie()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var seed = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var token = ExtractToken(await seed.Content.ReadAsStringAsync());

        await client.PostAsync(new Uri("/visuauth/culture", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["culture"] = "pt-BR",
                ["returnUrl"] = "/visuauth/login",
            }));

        var follow = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await follow.Content.ReadAsStringAsync();

        body.Should().Contain(">Entrar</h1>",
            "the cookie persisted by /visuauth/culture must drive subsequent requests");
    }

    [Fact]
    public async Task PostCulture_WithOffSiteReturnUrl_FallsBackToRoot()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        var seed = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var token = ExtractToken(await seed.Content.ReadAsStringAsync());

        var response = await client.PostAsync(new Uri("/visuauth/culture", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["culture"] = "pt-BR",
                ["returnUrl"] = "https://evil.example.com/path",
            }));

        // Open-redirect guard: the response must redirect locally, not to
        // the attacker-controlled URL.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task PostCulture_WithUnsupportedCulture_DoesNotWriteCookie()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        var seed = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var token = ExtractToken(await seed.Content.ReadAsStringAsync());

        var response = await client.PostAsync(new Uri("/visuauth/culture", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["culture"] = "fr-ZZ",   // not in SupportedCultures
                ["returnUrl"] = "/visuauth/login",
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        // Endpoint silently ignores unknown cultures rather than crashing.
        response.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .Should().NotContain(c => c.StartsWith(".AspNetCore.Culture=", StringComparison.OrdinalIgnoreCase),
                "an unsupported culture must not be persisted");
    }

    [Fact]
    public async Task GetAdmin_SidebarRendersLanguageSwitcher()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("class=\"va-lang-switcher",
            "the admin layout must mount the language switcher tag helper");
        body.Should().Contain("action=\"/visuauth/culture\"",
            "the switcher must post to the culture endpoint");
        body.Should().Contain("name=\"culture\"",
            "the select must use the configured form field name");
        body.Should().Contain("value=\"pt-BR\"",
            "all supported cultures must appear as options");
    }

    [Fact]
    public async Task GetLogin_EndUserCardRendersLanguageSwitcher()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("va-enduser-lang",
            "the end-user layout must mount the language switcher too");
        body.Should().Contain("action=\"/visuauth/culture\"");
    }

    private static string ExtractToken(string html)
    {
        var match = TokenRegex().Match(html);
        match.Success.Should().BeTrue("anti-forgery token must be present in the page");
        return match.Groups[1].Value;
    }
}
