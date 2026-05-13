using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;
using Xunit;

namespace VisuAuth.IntegrationTests.MultiTenancy;

/// <summary>
/// Integration tests for multi-tenancy: header-based tenant resolution,
/// EF Core query-filter isolation, and the save-changes interceptor's
/// auto-population of <c>TenantId</c>.
/// </summary>
public sealed class MultiTenancyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly WebApplicationFactory<Program> _factory;

    public MultiTenancyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetUsers_WithAcmeTenantHeader_HidesOtherTenants()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("alice.silva@example.com",
            "alice is seeded in 'acme' and must show under the acme scope");
        body.Should().NotContain("daniel.eloi@example.com",
            "daniel is seeded in 'globex' and must be filtered out under acme");
        body.Should().NotContain("hugo.iglesias@example.com",
            "hugo is seeded in 'initech' and must be filtered out under acme");
    }

    [Fact]
    public async Task GetUsers_WithoutTenantHeader_ReturnsAllTenants()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("alice.silva@example.com");
        body.Should().Contain("daniel.eloi@example.com");
        body.Should().Contain("hugo.iglesias@example.com");
    }

    [Fact]
    public async Task GetUsers_WithMultiTenancyOn_RendersTenantColumn()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("<th>Tenant</th>",
            "the users list must surface a Tenant column when the host opts into tenancy");
        body.Should().MatchRegex(@"<code class=""va-tenant-id"">acme</code>",
            "seeded user tenants must render as monospace badges");
    }

    [Fact]
    public async Task Sidebar_WithResolvedTenant_ShowsIndicator()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");

        var response = await client.GetAsync(new Uri("/visuauth/admin/users", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("va-tenant-indicator",
            "the sidebar must render the tenant scope badge when multi-tenancy is on");
        body.Should().Contain(">globex<",
            "the resolved tenant id must appear in the sidebar badge");
    }

    [Fact]
    public async Task GetDetail_OnUserFromDifferentTenant_Returns404()
    {
        // Look up a globex user's id first.
        string globexUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync("daniel.eloi@example.com");
            user.Should().NotBeNull();
            globexUserId = user!.Id;
        }

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync(
            new Uri($"/visuauth/admin/users/{globexUserId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an acme-scoped admin must not see a globex user's detail");
    }

    [Fact]
    public async Task PostNewUser_WithTenantScope_InheritsTenantIdViaInterceptor()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "initech");

        var email = $"tenant.{Guid.NewGuid():N}@example.com";

        var token = await GetTokenAsync(client, "/visuauth/admin/users/new");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Form.Email"] = email,
            ["Form.UserName"] = "",
            ["Form.Password"] = "Tenant!Pass1",
            ["Form.PhoneNumber"] = "",
            ["Form.EmailConfirmed"] = "true",
        });

        var response = await client.PostAsync(new Uri("/visuauth/admin/users/new", UriKind.Relative), form);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByEmailAsync(email);
        user.Should().NotBeNull();
        user!.TenantId.Should().Be("initech",
            "the interceptor must stamp TenantId from the resolved request scope");
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
