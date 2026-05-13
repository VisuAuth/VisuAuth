using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VisuAuth.IntegrationTests.EndUser;

/// <summary>
/// Cross-cutting regression: every page that renders a password input
/// must wrap it in <c>.va-password-wrap</c> and include the
/// <c>data-va-password-toggle</c> button. Catches accidental copy-paste of
/// a bare <c>&lt;input type="password"&gt;</c> without the toggle widget.
/// </summary>
public sealed class PasswordToggleTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PasswordToggleTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/visuauth/login", 1)]
    [InlineData("/visuauth/register", 2)]
    [InlineData("/visuauth/reset-password?email=anyone@example.com&token=anything", 2)]
    [InlineData("/visuauth/admin/users/new", 1)]
    public async Task Get_PageWithPasswordFields_RendersToggleWidgetForEachField(string url, int expectedToggleCount)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();

        var wrapMatches = Regex.Count(body, "va-password-wrap");
        var toggleMatches = Regex.Count(body, "data-va-password-toggle");
        var hiddenEyeOffMatches = Regex.Count(body, @"<svg class=""va-icon-eye-off""[^>]*\bhidden\b");

        wrapMatches.Should().Be(expectedToggleCount,
            $"{url} must wrap every password input in .va-password-wrap");
        toggleMatches.Should().Be(expectedToggleCount,
            $"{url} must render a [data-va-password-toggle] button for every password input");
        hiddenEyeOffMatches.Should().Be(expectedToggleCount,
            $"{url} must mark every .va-icon-eye-off SVG with the `hidden` attribute on initial render so both icons never show at once");

        // Sanity: there must not be any bare <input type="password"> left
        // outside the wrapper — that would indicate a stale copy-paste.
        body.Should().NotMatchRegex(
            @"<input[^>]*type=""password""[^>]*>\s*</label>",
            $"{url} must not have any password input that escapes the toggle wrapper");
    }

    /// <summary>
    /// The password toggle is wired up by <c>visuauth.js</c>, which lives
    /// in <c>VisuAuth.AdminUi</c> and must be referenced from BOTH layouts
    /// (admin and end-user). A previous regression dropped the script tag
    /// from <c>_EndUserLayout.cshtml</c>, leaving the toggle markup inert
    /// on the public sign-in pages. Lock that in.
    /// </summary>
    [Theory]
    [InlineData("/visuauth/login")]
    [InlineData("/visuauth/register")]
    [InlineData("/visuauth/reset-password?email=anyone@example.com&token=anything")]
    [InlineData("/visuauth/admin/users/new")]
    [InlineData("/visuauth/admin/users")]
    public async Task Get_PageWithLayout_IncludesSharedVisuAuthScript(string url)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();

        body.Should().MatchRegex(
            @"<script[^>]+src=""/_content/VisuAuth\.AdminUi/visuauth\.js",
            $"{url} must reference the shared visuauth.js so client-side widgets (password toggle, copy-to-clipboard) bind");
    }
}
