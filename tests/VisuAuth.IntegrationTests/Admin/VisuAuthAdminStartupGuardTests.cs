using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.AdminUi.DependencyInjection;
using VisuAuth.AdminUi.Localization;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// The admin dashboard is secured by default, which is worthless if the host
/// has no authentication scheme to challenge with — the request just dies with
/// "No authenticationScheme was specified". These tests pin the guard that
/// turns that into a startup failure with an actionable message.
/// </summary>
public sealed class VisuAuthAdminStartupGuardTests
{
    [Fact]
    public async Task Startup_WithSecuredAdminAndNoAuthenticationScheme_FailsWithAnActionableError()
    {
        using var app = BuildApp(services => { });

        var start = async () => await app.StartAsync();

        (await start.Should().ThrowAsync<InvalidOperationException>(
                "a secured admin with nothing to challenge with can only ever 500 at runtime"))
            .WithMessage("*AllowAnonymousVisuAuthAdmin*", "the error must name the escape hatch")
            .And.Message.Should().Contain("AddVisuAuthEntraSignIn",
                "Entra is the path where this misconfiguration is easiest to hit");
    }

    [Fact]
    public async Task Startup_WithAnAuthenticationScheme_Succeeds()
    {
        using var app = BuildApp(services =>
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie());

        var start = async () => await app.StartAsync();

        await start.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Startup_WithAnonymousAdminOptOut_Succeeds()
    {
        // The consumer deliberately dropped the gate, so there is nothing to
        // challenge with and nothing to complain about.
        using var app = BuildApp(services => services.AllowAnonymousVisuAuthAdmin());

        var start = async () => await app.StartAsync();

        await start.Should().NotThrowAsync();
    }

    private static WebApplication BuildApp(Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddVisuAuthLocalization();
        builder.Services.AddVisuAuthAdminUi();
        configure(builder.Services);
        return builder.Build();
    }
}
