extern alias EntraExternalSample;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace VisuAuth.IntegrationTests.Samples;

/// <summary>
/// Smoke coverage for the Entra External ID sample — the companion to
/// <see cref="EntraSampleSmokeTests"/>. Both exist because the suite used to
/// boot only <c>Sample.WebApp</c>, so nothing noticed when securing the admin
/// by default left an Entra deployment with no way to sign in.
/// </summary>
/// <remarks>
/// External already shipped a sign-in package (<c>VisuAuth.EntraExternal.Web</c>),
/// which is why it never had the Workforce sample's 500 — this pins that.
/// As with the Workforce sample, no OIDC round-trip is driven: that needs a real
/// tenant. The assertions are the offline-deterministic ones.
/// </remarks>
public sealed class EntraExternalSampleSmokeTests(EntraExternalSampleSmokeTests.EntraExternalSampleFactory factory)
    : IClassFixture<EntraExternalSampleSmokeTests.EntraExternalSampleFactory>
{
    private readonly EntraExternalSampleFactory _factory = factory;

    [Fact]
    public async Task GetHome_OnTheEntraExternalSample_Returns200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the Entra External sample must boot and serve its anonymous launcher page");
    }

    [Fact]
    public async Task EntraExternalSample_RegistersADefaultChallengeScheme()
    {
        // Same regression guard as the Workforce sample: without a default
        // challenge scheme the secured admin cannot redirect and every request
        // dies with "No authenticationScheme was specified".
        var schemes = _factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        var challengeScheme = await schemes.GetDefaultChallengeSchemeAsync();

        challengeScheme.Should().NotBeNull(
            "AddVisuAuthEntraExternalSignIn must leave the admin gate something to challenge with");
    }

    /// <summary>
    /// Boots the External sample with placeholder credentials — enough to
    /// satisfy the options validators; no Graph or OIDC call is made.
    /// </summary>
    public sealed class EntraExternalSampleFactory : WebApplicationFactory<EntraExternalSample::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // UseSetting, not ConfigureAppConfiguration: the sample reads
            // builder.Configuration while registering services, which runs
            // before ConfigureAppConfiguration callbacks would apply.
            builder.UseSetting("VisuAuth:EntraExternal:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("VisuAuth:EntraExternal:ClientId", "00000000-0000-0000-0000-000000000002");
            builder.UseSetting("VisuAuth:EntraExternal:ClientSecret", "placeholder-graph-secret");
            builder.UseSetting("VisuAuth:EntraExternal:TenantDomain", "placeholder.onmicrosoft.com");
            builder.UseSetting("VisuAuth:EntraExternal:Web:TenantSubdomain", "placeholder");
            builder.UseSetting("VisuAuth:EntraExternal:Web:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("VisuAuth:EntraExternal:Web:ClientId", "00000000-0000-0000-0000-000000000003");
            builder.UseSetting("VisuAuth:EntraExternal:Web:ClientSecret", "placeholder-signin-secret");
        }
    }
}
