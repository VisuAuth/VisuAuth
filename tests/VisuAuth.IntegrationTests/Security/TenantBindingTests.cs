using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace VisuAuth.IntegrationTests.Security;

/// <summary>
/// Adversarial coverage for tenant isolation: a bearer-authenticated caller
/// must be bound to the tenant in their signed token, and must not be able to
/// escape it by sending an <c>X-Tenant-Id</c> header for someone else's tenant.
/// </summary>
public sealed class TenantBindingTests : IClassFixture<VisuAuthTestFactory>
{
    private static readonly Uri LoginUri = new("/visuauth/api/auth/login", UriKind.Relative);
    private static readonly Uri MeUri = new("/api/me", UriKind.Relative);

    // Seeded into tenant 'acme'.
    private const string Email = "alice.silva@example.com";
    private const string Password = "Pa$$w0rd!";

    private readonly VisuAuthTestFactory _factory;

    public TenantBindingTests(VisuAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProtected_WithBearer_ResolvesTenantFromTheSignedClaim()
    {
        using var client = _factory.CreateClient();
        var token = await SignInAsync(client);

        var body = await GetMeAsync(client, token, tenantHeader: null);

        body.GetProperty("tenant").GetString().Should().Be("acme");
    }

    [Fact]
    public async Task GetProtected_WithBearerAndForeignTenantHeader_IgnoresTheHeader()
    {
        using var client = _factory.CreateClient();
        var token = await SignInAsync(client);

        // The attack: a genuine acme token plus a header claiming globex.
        // The signed claim must win, or a user could operate in another
        // tenant's scope just by setting a request header.
        var body = await GetMeAsync(client, token, tenantHeader: "globex");

        body.GetProperty("tenant").GetString().Should().Be("acme",
            "the signed tenant_id claim is authoritative for token-authenticated callers");
    }

    private async Task<JsonElement> GetMeAsync(HttpClient client, string token, string? tenantHeader)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUri);
        request.Headers.Add("Authorization", $"Bearer {token}");
        if (tenantHeader is not null)
        {
            request.Headers.Add("X-Tenant-Id", tenantHeader);
        }

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<string> SignInAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(LoginUri, new { email = Email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }
}
