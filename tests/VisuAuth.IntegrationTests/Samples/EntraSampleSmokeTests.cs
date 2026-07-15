extern alias EntraSample;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace VisuAuth.IntegrationTests.Samples;

/// <summary>
/// Smoke coverage for the Entra ID (Workforce) sample. Until this existed the
/// integration suite only ever booted <c>Sample.WebApp</c>, which is how the
/// Entra admin came to return 500 on every page — securing the dashboard by
/// default gave the authorization gate nothing to challenge with, because the
/// Entra adapter registers no authentication scheme on its own.
/// </summary>
/// <remarks>
/// These tests deliberately do not drive an OIDC round-trip: that would require
/// a real tenant and a network call to Microsoft. They assert the things that
/// are deterministic offline — that the sample boots, serves anonymous pages,
/// and (the actual regression guard) has a default challenge scheme so the
/// admin gate can redirect instead of throwing.
/// </remarks>
public sealed class EntraSampleSmokeTests(EntraSampleSmokeTests.EntraSampleFactory factory)
    : IClassFixture<EntraSampleSmokeTests.EntraSampleFactory>
{
    private readonly EntraSampleFactory _factory = factory;

    [Fact]
    public async Task GetHome_OnTheEntraSample_Returns200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the Entra sample must boot and serve its anonymous launcher page");
    }

    [Fact]
    public async Task EntraSample_RegistersADefaultChallengeScheme()
    {
        // The regression guard. AddVisuAuthEntra wires Graph with app-only
        // credentials — that authenticates the *app*, not a human, and leaves
        // the app with no authentication scheme. With the admin secured by
        // default, a missing challenge scheme means every admin request dies
        // with "No authenticationScheme was specified, and there was no
        // DefaultChallengeScheme found". AddVisuAuthEntraSignIn is what makes
        // the gate able to challenge.
        var schemes = _factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        var challengeScheme = await schemes.GetDefaultChallengeSchemeAsync();

        challengeScheme.Should().NotBeNull(
            "the admin gate has to have something to challenge with, or every admin page 500s");
    }

    /// <summary>
    /// Boots the Entra sample with placeholder credentials. They only need to
    /// satisfy the options validators — no Graph or OIDC call is made, since
    /// nothing here drives an authenticated flow.
    /// </summary>
    public sealed class EntraSampleFactory : WebApplicationFactory<EntraSample::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // UseSetting, not ConfigureAppConfiguration: the sample reads
            // builder.Configuration while registering services, which happens
            // before ConfigureAppConfiguration callbacks run — the values would
            // arrive too late and Microsoft.Identity.Web would fail with
            // "IDW10106: The 'ClientId' option must be provided". UseSetting
            // feeds host configuration, which is in place from the start.
            builder.UseSetting("VisuAuth:Entra:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("VisuAuth:Entra:ClientId", "00000000-0000-0000-0000-000000000002");
            builder.UseSetting("VisuAuth:Entra:ClientSecret", "placeholder-graph-secret");
            builder.UseSetting("VisuAuth:Entra:Web:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("VisuAuth:Entra:Web:ClientId", "00000000-0000-0000-0000-000000000003");
            builder.UseSetting("VisuAuth:Entra:Web:ClientSecret", "placeholder-signin-secret");
        }
    }
}
