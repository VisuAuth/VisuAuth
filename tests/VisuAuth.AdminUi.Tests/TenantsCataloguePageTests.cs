using System.Net;
using System.Text.RegularExpressions;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sample.WebApp.Data;

using VisuAuth.Identity.MultiTenancy;

using Xunit;

namespace VisuAuth.AdminUi.Tests;

/// <summary>
/// Integration tests for <c>/visuauth/admin/tenants</c> and the sidebar
/// tenant switcher cookie handler.
/// </summary>
public sealed class TenantsCataloguePageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public TenantsCataloguePageTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Catalogue_renders_seeded_tenants_with_member_counts()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/tenants", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Acme Corporation",
            "the seeded acme tenant must show its display name");
        body.Should().Contain("Globex Industries");
        body.Should().Contain("Initech Solutions");

        // Each seeded tenant has 4 members.
        body.Should().MatchRegex(@"<code class=""va-tenant-id"">acme</code>[\s\S]*?<span class=""va-badge"">4</span>",
            "acme must show 4 members from the seeder");
    }

    [Fact]
    public async Task Create_handler_adds_a_tenant()
    {
        using var client = CreateClient();
        var id = $"t{Guid.NewGuid():N}"[..10];

        var token = await GetTokenAsync(client, "/visuauth/admin/tenants");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["NewTenantId"] = id,
            ["NewTenantDisplayName"] = "Sample Tenant",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Create", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain($"Tenant &#x27;{id}&#x27; created.");
        body.Should().Contain("Sample Tenant");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.VisuAuthTenants.AnyAsync(t => t.Id == id)).Should().BeTrue();
    }

    [Fact]
    public async Task Create_with_invalid_id_surfaces_validation_error()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/tenants");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["NewTenantId"] = "has spaces!",
            ["NewTenantDisplayName"] = "Bad",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Create", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("must be 1-64 characters",
            "the store rejects ids that would not survive a URL");
    }

    [Fact]
    public async Task Delete_handler_refuses_to_remove_a_tenant_with_members()
    {
        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/tenants");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = "acme",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Delete", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("still has", "deleting an occupied tenant must surface a guarded error");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.VisuAuthTenants.AnyAsync(t => t.Id == "acme")).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_handler_removes_empty_tenants()
    {
        // Provision a throwaway empty tenant.
        var id = $"empty{Guid.NewGuid():N}"[..12];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VisuAuthTenants.Add(new VisuAuthTenant
            {
                Id = id,
                DisplayName = "Empty Co",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        var token = await GetTokenAsync(client, "/visuauth/admin/tenants");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = id,
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Delete", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain($"Tenant &#x27;{id}&#x27; deleted.");

        using var verify = _factory.Services.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db2.VisuAuthTenants.AnyAsync(t => t.Id == id)).Should().BeFalse();
    }

    [Fact]
    public async Task Sidebar_switcher_renders_tenants_for_browser_scoping()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("va-tenant-switcher",
            "the sidebar must render the tenant scope dropdown when multi-tenancy is on");
        body.Should().Contain("value=\"acme\"");
        body.Should().Contain("value=\"globex\"");
        body.Should().Contain("value=\"initech\"");
        body.Should().Contain(">Tenants<",
            "the sidebar nav must promote the Tenants link to a real entry");
    }

    [Fact]
    public async Task Sidebar_switcher_form_includes_its_own_antiforgery_token()
    {
        // Browser-driven submissions only carry the inputs of the form being
        // submitted — the form tag helper must inject the antiforgery token
        // into the sidebar switcher itself, otherwise clicking the dropdown
        // would 400.
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        var switcherFormMatch = Regex.Match(
            body,
            @"<form[^>]*\bclass=""va-tenant-indicator""[^>]*>([\s\S]*?)</form>");
        switcherFormMatch.Success.Should().BeTrue("the sidebar switcher form must be rendered");

        switcherFormMatch.Groups[1].Value.Should().Contain("__RequestVerificationToken",
            "the switcher form must carry an antiforgery token so browser submissions are accepted");
    }

    [Fact]
    public async Task Switch_handler_sets_the_tenant_cookie_and_redirects_to_returnUrl()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var token = await GetTokenAsync(client, "/visuauth/admin/tenants");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["tenantId"] = "globex",
            ["returnUrl"] = "/visuauth/admin/users",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Switch", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/admin/users");

        // After the redirect the client cookie jar holds va-tenant=globex —
        // a follow-up request to /users must filter to globex.
        var followUp = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await followUp.Content.ReadAsStringAsync();
        body.Should().Contain("daniel.eloi@example.com",
            "the cookie should keep the globex scope across requests");
        body.Should().NotContain("alice.silva@example.com",
            "acme users must be filtered out once the cookie is set to globex");
    }

    [Fact]
    public async Task Switch_handler_with_empty_tenant_clears_the_cookie()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        // Set then clear.
        var token = await GetTokenAsync(client, "/visuauth/admin/tenants");
        await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Switch", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["tenantId"] = "acme",
                ["returnUrl"] = "/visuauth/admin/users",
            }));

        token = await GetTokenAsync(client, "/visuauth/admin/tenants");
        var clear = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Switch", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["tenantId"] = "",
                ["returnUrl"] = "/visuauth/admin/users",
            }));

        clear.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // Cleared cookie → tenant filter is off → both tenants visible again.
        var followUp = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await followUp.Content.ReadAsStringAsync();
        body.Should().Contain("alice.silva@example.com");
        body.Should().Contain("daniel.eloi@example.com");
    }

    [Fact]
    public async Task Switch_handler_rejects_non_local_return_urls()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var token = await GetTokenAsync(client, "/visuauth/admin/tenants");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["tenantId"] = "acme",
            ["returnUrl"] = "https://attacker.example/phish",
        });

        var response = await client.PostAsync(
            new Uri("/visuauth/admin/tenants?handler=Switch", UriKind.Relative),
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/visuauth/admin/users",
            "non-local returnUrl values must fall back to a safe default");
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
    });

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue($"{url} must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }
}
