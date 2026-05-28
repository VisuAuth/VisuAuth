using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Http.HttpClientLibrary;
using VisuAuth.EntraExternal.Web;
using VisuAuth.EntraExternal.Web.Configuration;
using VisuAuth.UnitTests.Entra.Internal;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal.Web;

/// <summary>
/// Behaviour pin for <see cref="EntraExternalProfileSync"/> — the bit
/// that copies id_token claims onto the Graph user on sign-in. Uses the
/// shared <see cref="FakeGraphHandler"/> so the PATCH goes through the
/// real Kiota serialisation pipeline; the handler captures the request
/// body so we can assert exactly which properties were written.
/// </summary>
public sealed class EntraExternalProfileSyncTests
{
    private const string Oid = "00000000-0000-0000-0000-0000000000fe";

    [Fact]
    public async Task SyncAsync_Disabled_DoesNotCallGraph()
    {
        var handler = new FakeGraphHandler(); // no routes — any call throws
        var sut = BuildSync(handler, enabled: false);

        await sut.SyncAsync(PrincipalWith(("given_name", "Alice")), Oid);

        handler.RecordedRequests.Should().BeEmpty("profile sync is off → no PATCH should be issued");
    }

    [Fact]
    public async Task SyncAsync_Enabled_NoMappedClaimsPresent_DoesNotCallGraph()
    {
        var handler = new FakeGraphHandler();
        var sut = BuildSync(handler, enabled: true);

        // Principal carries only claims that aren't in the mapping.
        await sut.SyncAsync(PrincipalWith(("sub", "x"), ("aud", "y")), Oid);

        handler.RecordedRequests.Should().BeEmpty("nothing on the token maps to a Graph property → skip the round-trip");
    }

    [Fact]
    public async Task SyncAsync_Enabled_PatchesMappedNameClaims()
    {
        var handler = new FakeGraphHandler().SetupPatch($"/users/{Oid}");
        var sut = BuildSync(handler, enabled: true);

        await sut.SyncAsync(PrincipalWith(("given_name", "Alice"), ("family_name", "Silva")), Oid);

        var body = await ReadPatchBodyAsync(handler, Oid);
        body.GetProperty("givenName").GetString().Should().Be("Alice");
        body.GetProperty("surname").GetString().Should().Be("Silva");
    }

    [Fact]
    public async Task SyncAsync_OnlyWritesPropertiesWhoseClaimIsPresent()
    {
        var handler = new FakeGraphHandler().SetupPatch($"/users/{Oid}");
        var sut = BuildSync(handler, enabled: true);

        // Only given_name present — surname must NOT appear in the patch.
        await sut.SyncAsync(PrincipalWith(("given_name", "Alice")), Oid);

        var body = await ReadPatchBodyAsync(handler, Oid);
        body.GetProperty("givenName").GetString().Should().Be("Alice");
        body.TryGetProperty("surname", out _).Should().BeFalse(
            "a property with no corresponding claim must be left untouched, not cleared");
    }

    [Fact]
    public async Task SyncAsync_CustomMapping_WritesToConfiguredGraphProperty()
    {
        var handler = new FakeGraphHandler().SetupPatch($"/users/{Oid}");
        var sut = BuildSync(handler, enabled: true, configure: o =>
            o.ClaimToGraphProperty["extension_app_country"] = "country");

        await sut.SyncAsync(PrincipalWith(("extension_app_country", "BR")), Oid);

        var body = await ReadPatchBodyAsync(handler, Oid);
        body.GetProperty("country").GetString().Should().Be("BR");
    }

    [Fact]
    public async Task SyncAsync_MapsEverySupportedTargetProperty()
    {
        // Exercises every branch of the target-property allow-list in one
        // PATCH: configure a claim per supported property and assert each
        // lands. (Also keeps the switch fully covered.)
        var supported = new (string Claim, string GraphProp, string Value)[]
        {
            ("c_given", "givenName", "Alice"),
            ("c_surname", "surname", "Silva"),
            ("c_display", "displayName", "Alice Silva"),
            ("c_job", "jobTitle", "Engineer"),
            ("c_dept", "department", "Platform"),
            ("c_company", "companyName", "Contoso"),
            ("c_city", "city", "São Paulo"),
            ("c_state", "state", "SP"),
            ("c_country", "country", "BR"),
            ("c_postal", "postalCode", "01000-000"),
            ("c_street", "streetAddress", "Av. Paulista 1000"),
        };

        var handler = new FakeGraphHandler().SetupPatch($"/users/{Oid}");
        var sut = BuildSync(handler, enabled: true, configure: o =>
        {
            o.ClaimToGraphProperty.Clear();
            foreach (var (claim, graphProp, _) in supported)
            {
                o.ClaimToGraphProperty[claim] = graphProp;
            }
        });

        await sut.SyncAsync(PrincipalWith(supported.Select(s => (s.Claim, s.Value)).ToArray()), Oid);

        var body = await ReadPatchBodyAsync(handler, Oid);
        foreach (var (_, graphProp, value) in supported)
        {
            body.GetProperty(graphProp).GetString().Should().Be(value,
                $"the '{graphProp}' target must be written from its mapped claim");
        }
    }

    [Fact]
    public async Task SyncAsync_UnsupportedTargetProperty_IsSkipped_NoThrow()
    {
        // A typo / unsupported Graph property in the mapping must be
        // skipped, not crash the PATCH (and not block sign-in).
        var handler = new FakeGraphHandler().SetupPatch($"/users/{Oid}");
        var sut = BuildSync(handler, enabled: true, configure: o =>
        {
            o.ClaimToGraphProperty.Clear();
            o.ClaimToGraphProperty["given_name"] = "givenName";
            o.ClaimToGraphProperty["some_claim"] = "notARealGraphProperty";
        });

        await sut.SyncAsync(PrincipalWith(("given_name", "Alice"), ("some_claim", "ignored")), Oid);

        var body = await ReadPatchBodyAsync(handler, Oid);
        body.GetProperty("givenName").GetString().Should().Be("Alice");
        body.TryGetProperty("notARealGraphProperty", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SyncAsync_AliasClaim_ResolvesLegacySoapUriForNameClaims()
    {
        // When the OIDC handler maps inbound claims, given_name arrives as
        // the legacy SOAP URI. The default mapping must still find it.
        var handler = new FakeGraphHandler().SetupPatch($"/users/{Oid}");
        var sut = BuildSync(handler, enabled: true);

        await sut.SyncAsync(PrincipalWith((ClaimTypes.GivenName, "Alice")), Oid);

        var body = await ReadPatchBodyAsync(handler, Oid);
        body.GetProperty("givenName").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task SyncAsync_GraphError_DoesNotThrow()
    {
        // Best-effort contract: a Graph failure must be swallowed so it
        // never breaks the sign-in the user already completed.
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Patch, $"/users/{Oid}", HttpStatusCode.Forbidden,
                "Authorization_RequestDenied", "Insufficient privileges.");
        var sut = BuildSync(handler, enabled: true);

        var act = () => sut.SyncAsync(PrincipalWith(("given_name", "Alice")), Oid);

        await act.Should().NotThrowAsync("profile sync is best-effort and must not break sign-in");
    }

    [Fact]
    public async Task SyncAsync_BlankUserId_DoesNotCallGraph()
    {
        var handler = new FakeGraphHandler();
        var sut = BuildSync(handler, enabled: true);

        await sut.SyncAsync(PrincipalWith(("given_name", "Alice")), userObjectId: "");

        handler.RecordedRequests.Should().BeEmpty();
    }

    // ---- helpers --------------------------------------------------------

    private static Task<JsonElement> ReadPatchBodyAsync(FakeGraphHandler handler, string oid)
    {
        // Find the PATCH request and read its index-aligned buffered body
        // (request.Content is already disposed by the time we get here).
        var index = handler.RecordedRequests.FindIndex(r =>
            r.Method == HttpMethod.Patch && (r.RequestUri?.AbsolutePath.Contains(oid, StringComparison.Ordinal) ?? false));
        index.Should().BeGreaterThanOrEqualTo(0, "the sync must have issued a PATCH for the user");
        var json = handler.RecordedRequestBodies[index];
        json.Should().NotBeNullOrEmpty("the PATCH must carry a JSON body");
        return Task.FromResult(JsonDocument.Parse(json!).RootElement);
    }

    private static ClaimsPrincipal PrincipalWith(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "TestOidc"));

    private static EntraExternalProfileSync BuildSync(
        FakeGraphHandler handler,
        bool enabled,
        Action<EntraExternalProfileSyncOptions>? configure = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthProvider(), httpClient: httpClient);
        var graph = new GraphServiceClient(adapter);

        var webOptions = new EntraExternalWebOptions
        {
            TenantSubdomain = "contoso",
            TenantId = "t",
            ClientId = "c",
        };
        webOptions.ProfileSync.Enabled = enabled;
        configure?.Invoke(webOptions.ProfileSync);

        return new EntraExternalProfileSync(
            graph,
            Options.Create(webOptions),
            NullLogger<EntraExternalProfileSync>.Instance);
    }
}
