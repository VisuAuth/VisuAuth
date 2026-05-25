using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.Abstractions.Authentication;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Integration tests for <c>/visuauth/admin/external-providers</c>. Covers
/// the list rendering (seeded providers visible), the save round-trip
/// (DB row updated + options cache invalidated), the bulk toggle, and the
/// /visuauth/login provider-button gate that mirrors IsEnabled state.
/// </summary>
public sealed partial class ExternalProvidersPageTests(VisuAuthTestFactory factory) : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Regex TokenRegex = TokenRegexImpl();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex TokenRegexImpl();

    private readonly VisuAuthTestFactory _factory = factory;

    [Fact]
    public async Task GetIndex_RendersSeededProvidersFromTheStore()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/external-providers", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("External providers", "the page heading must render");
        // Sample's UserSeeder pre-populates these four schemes; the list
        // must surface every one of them even when they're configured-empty.
        body.Should().Contain(">Microsoft<");
        body.Should().Contain(">Google<");
        body.Should().Contain(">GitHub<");
        body.Should().Contain(">Apple<");
    }

    [Fact]
    public async Task PostSave_PersistsClientIdAndEnablesScheme()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/external-providers");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["EditFields.ClientId"] = "test-microsoft-id",
            ["EditFields.ClientSecret"] = "test-microsoft-secret",
            ["EditFields.IsEnabled"] = "true",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/external-providers?handler=Save&scheme=Microsoft", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        // Razor encodes the apostrophe as &#x27; — check both halves of the
        // "Saved 'Microsoft'" message instead of the exact rendered string.
        body.Should().Contain("Saved");
        body.Should().Contain("Microsoft");

        // Verify the store actually persisted.
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
        var view = await store.GetAsync("Microsoft", tenantId: null);
        view.Should().NotBeNull();
        view!.ClientId.Should().Be("test-microsoft-id");
        view.IsEnabled.Should().BeTrue();
        view.HasClientSecret.Should().BeTrue();
        // The plaintext secret must NOT be readable from the view (only
        // through GetClientSecretAsync server-side).
        body.Should().NotContain("test-microsoft-secret",
            "the response must never re-render the plaintext secret");
    }

    [Fact]
    public async Task PostSave_WithBlankSecret_PreservesExistingCiphertext()
    {
        // Pre-populate via the store directly.
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
            await store.SaveAsync(new SaveExternalProviderConfigCommand
            {
                Scheme = "Google",
                DisplayName = "Google",
                ClientId = "google-original",
                PlainTextClientSecret = "google-original-secret",
                IsEnabled = true,
            });
        }

        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/external-providers");
        // Admin edits only ClientId, leaves secret field BLANK.
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["EditFields.ClientId"] = "google-renamed",
            ["EditFields.ClientSecret"] = string.Empty,
            ["EditFields.IsEnabled"] = "true",
        });
        var response = await client.PostAsync(
            new Uri("/visuauth/admin/external-providers?handler=Save&scheme=Google", UriKind.Relative),
            form);
        response.IsSuccessStatusCode.Should().BeTrue();

        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
        var refreshed = await verifier.GetAsync("Google", tenantId: null);
        refreshed!.ClientId.Should().Be("google-renamed");
        refreshed.HasClientSecret.Should().BeTrue(
            "leaving the secret field blank must preserve the previously stored ciphertext");
        var secret = await verifier.GetClientSecretAsync("Google", tenantId: null);
        secret.Should().Be("google-original-secret",
            "plaintext round-trips must still match after a partial-edit save");
    }

    [Fact]
    public async Task PostBulkDisable_FlipsEveryRowToDisabled()
    {
        // Enable everything first.
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
            foreach (var s in new[] { "Microsoft", "Google", "GitHub", "Apple" })
            {
                await store.SaveAsync(new SaveExternalProviderConfigCommand
                {
                    Scheme = s,
                    DisplayName = s,
                    ClientId = $"{s}-id",
                    PlainTextClientSecret = $"{s}-secret",
                    IsEnabled = true,
                });
            }
        }

        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/external-providers");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync(
            new Uri("/visuauth/admin/external-providers?handler=BulkDisable", UriKind.Relative),
            form);
        response.IsSuccessStatusCode.Should().BeTrue();

        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
        var all = await verifier.ListAsync(tenantId: null);
        all.Should().AllSatisfy(c => c.IsEnabled.Should().BeFalse(
            "BulkDisable must flip every row regardless of its previous state"));
    }

    [Fact]
    public async Task GetIndex_RendersFromDbBadge_ForActiveProviderWithStoredCredentials()
    {
        // Seed a DB row so the page model picks it up and the partial
        // renders the "from DB" badge alongside the Client ID cell.
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
            await store.SaveAsync(new SaveExternalProviderConfigCommand
            {
                Scheme = "Microsoft",
                DisplayName = "Microsoft",
                ClientId = "ms-from-db-test",
                PlainTextClientSecret = "ms-secret-from-db",
                IsEnabled = true,
            });
        }

        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/external-providers", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("ms-from-db-test", "the saved Client ID must render in the table");
        body.Should().Contain("va-source-badge-db",
            "the 'from DB' badge must render when a value comes from the dynamic store");
        body.Should().Contain("from DB", "the badge label must render");
    }

    [Fact]
    public async Task GetIndex_RendersGhostCardsForCatalogueProvidersTheHostDidNotWire()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/external-providers", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();

        // The Sample wires Microsoft / Google / Apple / GitHub / Facebook.
        // The remaining ~15 catalogue entries must show up as ghost cards
        // under the Available section with the install snippet.
        body.Should().Contain("Available providers", "the ghost-card section heading must render");
        body.Should().Contain("LinkedIn", "LinkedIn is in the catalogue but not wired by the sample");
        body.Should().Contain("AspNet.Security.OAuth.LinkedIn",
            "the install snippet must show the NuGet package for LinkedIn");
        body.Should().Contain("AddLinkedIn",
            "the install snippet must show the fluent extension method");
        body.Should().Contain("Discord");
        body.Should().Contain("Slack");
        body.Should().Contain("How to activate", "the ghost-card details summary must render");
    }

    [Fact]
    public async Task PostSave_OnUnregisteredScheme_ReturnsErrorWithoutPersisting()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/external-providers");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["EditFields.ClientId"] = "linkedin-id",
            ["EditFields.ClientSecret"] = "linkedin-secret",
            ["EditFields.IsEnabled"] = "true",
        });

        // LinkedIn is in the catalogue but the Sample does NOT call
        // AddVisuAuthDynamicExternalProviderOptions<LinkedInAuthenticationOptions>
        // — saving here would dead-end (no handler, login button would never
        // render). The page model must reject this rather than write into the
        // void.
        var response = await client.PostAsync(
            new Uri("/visuauth/admin/external-providers?handler=Save&scheme=LinkedIn", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("not wired with AddVisuAuthDynamicExternalProviderOptions",
            "the page must explain why the save was rejected");

        // No row created.
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
        (await store.GetAsync("LinkedIn", tenantId: null)).Should().BeNull();
    }

    [Fact]
    public async Task PostDeleteOrphan_RemovesOrphanRowAndSurfacesSuccess()
    {
        // Manually insert a row for a scheme the host doesn't wire — simulates
        // the operator removing a Program.cs provider registration while a row
        // stays behind in the DB.
        const string OrphanScheme = "DroppedFromProgramCs";
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
            await store.SaveAsync(new SaveExternalProviderConfigCommand
            {
                Scheme = OrphanScheme,
                DisplayName = "Dropped",
                ClientId = "orphan-id",
                PlainTextClientSecret = "orphan-secret",
                IsEnabled = true,
            });
        }

        using var client = CreateClient();
        var getResponse = await client.GetAsync(new Uri("/visuauth/admin/external-providers", UriKind.Relative));
        var listingBody = await getResponse.Content.ReadAsStringAsync();
        listingBody.Should().Contain("Orphaned credentials",
            "the orphan section heading must render when a stray row exists");
        listingBody.Should().Contain(OrphanScheme,
            "the orphaned row must be listed so the admin can clean it up");

        var token = await GetTokenAsync(client, "/visuauth/admin/external-providers");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });
        var deleteResponse = await client.PostAsync(
            new Uri($"/visuauth/admin/external-providers?handler=DeleteOrphan&scheme={OrphanScheme}", UriKind.Relative),
            form);
        deleteResponse.IsSuccessStatusCode.Should().BeTrue();

        // Row really gone.
        using var verifyScope = _factory.Services.CreateScope();
        var verifier = verifyScope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
        (await verifier.GetAsync(OrphanScheme, tenantId: null)).Should().BeNull();
    }

    [Fact]
    public async Task GetLogin_AfterDisablingAllProviders_RendersNoExternalButtons()
    {
        // Disable every provider via the store.
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IExternalProviderConfigStore>();
            foreach (var s in new[] { "Microsoft", "Google", "GitHub", "Apple" })
            {
                await store.SetEnabledAsync(s, tenantId: null, isEnabled: false);
            }
        }

        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/login", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().NotContain("Continue with Microsoft",
            "GetProvidersAsync must filter out disabled schemes");
        body.Should().NotContain("Continue with Google");
        body.Should().NotContain("Continue with GitHub");
        body.Should().NotContain("Continue with Apple");
        body.Should().NotContain("class=\"va-divider\"",
            "the 'or' divider only renders when at least one provider is active");
    }

    private HttpClient CreateClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} must succeed (status {response.StatusCode})");
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue($"{url} must render an antiforgery-protected form");
        return match.Groups[1].Value;
    }
}
