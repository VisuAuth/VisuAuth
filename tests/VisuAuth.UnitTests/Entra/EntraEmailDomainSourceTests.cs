using System.Net;
using FluentAssertions;
using Microsoft.Graph;
using Microsoft.Kiota.Http.HttpClientLibrary;
using VisuAuth.Entra;
using VisuAuth.UnitTests.Entra.Internal;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Covers <see cref="EntraEmailDomainSource"/> against a
/// <see cref="FakeGraphHandler"/>: the real Kiota pipeline deserialises a
/// canned <c>/domains</c> payload, so the projection (verified-only, primary
/// first), the graceful 403 degrade, and the cache semantics all run
/// end-to-end without a live tenant.
/// </summary>
public sealed class EntraEmailDomainSourceTests
{
    private const string DomainsJson = """
        {
          "value": [
            { "id": "alias.contoso.com", "isVerified": true, "isDefault": false },
            { "id": "contoso.com", "isVerified": true, "isDefault": true },
            { "id": "contoso.onmicrosoft.com", "isVerified": true, "isDefault": false },
            { "id": "pending.contoso.com", "isVerified": false, "isDefault": false }
          ]
        }
        """;

    [Fact]
    public async Task GetEmailDomainsAsync_MultipleVerified_ReturnsVerifiedOnly_PrimaryFirstThenAlphabetical()
    {
        var handler = new FakeGraphHandler().SetupGet("/domains", DomainsJson);
        using var sut = BuildSource(handler);

        var domains = await sut.GetEmailDomainsAsync();

        // Primary (isDefault) leads; the unverified domain is filtered out;
        // the remaining verified domains are alphabetical.
        domains.Should().ContainInOrder("contoso.com", "alias.contoso.com", "contoso.onmicrosoft.com");
        domains.Should().NotContain("pending.contoso.com", "unverified domains are excluded");
    }

    [Fact]
    public async Task GetEmailDomainsAsync_OnForbidden_ReturnsEmpty()
    {
        // Missing Domain.Read.All surfaces as a 403 ODataError; the source
        // swallows it so the create-user form degrades to the single suffix.
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/domains", HttpStatusCode.Forbidden, "Authorization_RequestDenied", "Insufficient privileges.");
        using var sut = BuildSource(handler);

        var domains = await sut.GetEmailDomainsAsync();

        domains.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEmailDomainsAsync_NonEmptyResult_IsCached_SecondCallDoesNotHitGraph()
    {
        var handler = new FakeGraphHandler().SetupGet("/domains", DomainsJson);
        using var sut = BuildSource(handler);

        var first = await sut.GetEmailDomainsAsync();
        var second = await sut.GetEmailDomainsAsync();

        second.Should().BeEquivalentTo(first);
        handler.RecordedRequests.Should().ContainSingle("a populated list is cached for the process lifetime");
    }

    [Fact]
    public async Task GetEmailDomainsAsync_EmptyResult_IsNotCached_RetriesOnNextCall()
    {
        // A 403 (or any empty fetch) must not pin "no domains" for the whole
        // process — a later render after the permission is granted retries.
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/domains", HttpStatusCode.Forbidden, "Authorization_RequestDenied", "Insufficient privileges.");
        using var sut = BuildSource(handler);

        await sut.GetEmailDomainsAsync();
        await sut.GetEmailDomainsAsync();

        handler.RecordedRequests.Should().HaveCount(2, "an empty result is never cached, so each call re-queries Graph");
    }

    private static EntraEmailDomainSource BuildSource(FakeGraphHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthProvider(), httpClient: httpClient);
        var graph = new GraphServiceClient(adapter);
        return new EntraEmailDomainSource(graph);
    }
}
