using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Users;
using VisuAuth.EntraExternal.Web;
using VisuAuth.EntraExternal.Web.Configuration;
using VisuAuth.EntraExternal.Web.DependencyInjection;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal.Web;

/// <summary>
/// Verifies that
/// <see cref="VisuAuthEntraExternalSignInExtensions.AddVisuAuthEntraExternalSignIn(IServiceCollection, IConfiguration, string)"/>
/// and the lambda overload register the same OIDC + EntraExternalLoginFlow
/// service graph. The flow REPLACES the no-op stub registered by the
/// EntraCore package — those two halves of the contract are the most
/// regression-prone bits, so they get explicit pinning.
/// </summary>
public sealed class VisuAuthEntraExternalSignInExtensionsTests
{
    [Fact]
    public void AddVisuAuthEntraExternalSignIn_WithLambda_BindsOptionsAndRegistersFlow()
    {
        var services = BaseServices();
        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = Guid.NewGuid().ToString();
            o.ClientId = Guid.NewGuid().ToString();
        });

        AssertSignInSurfaceRegistered(services);
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_FromConfiguration_BindsOptionsAndRegistersFlow()
    {
        var services = BaseServices();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VisuAuth:EntraExternal:Web:TenantSubdomain"] = "contoso",
                ["VisuAuth:EntraExternal:Web:TenantId"] = "tenant-guid",
                ["VisuAuth:EntraExternal:Web:ClientId"] = "client-guid",
                ["VisuAuth:EntraExternal:Web:ClientSecret"] = "secret-val",
            })
            .Build();

        services.AddVisuAuthEntraExternalSignIn(config);

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<EntraExternalWebOptions>>().Value;
        opts.TenantSubdomain.Should().Be("contoso");
        opts.TenantId.Should().Be("tenant-guid");
        opts.ClientId.Should().Be("client-guid");
        opts.ClientSecret.Should().Be("secret-val");
        AssertSignInSurfaceRegistered(services);
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_ReplacesNoOpStub_WithRealFlow()
    {
        // The EntraCore package pre-registers a no-op IExternalLoginFlow so
        // an Entra-only deployment resolves DI cleanly. AddVisuAuthEntraExternalSignIn
        // must REPLACE that with the real implementation (not TryAdd, which
        // would silently leave the stub in place and the "Sign in with
        // Microsoft" button would never render).
        var services = BaseServices();

        // Pre-register a stub that mimics the EntraCore one — same scope,
        // same interface — so we can prove the registration was actually
        // overridden.
        services.AddScoped<IExternalLoginFlow>(_ => Mock.Of<IExternalLoginFlow>());

        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IExternalLoginFlow>()
            .Should().BeOfType<EntraExternalLoginFlow>(
                "AddVisuAuthEntraExternalSignIn must Replace the no-op stub, not TryAdd alongside it");
    }

    [Fact]
    public async Task AddVisuAuthEntraExternalSignIn_RegistersOidcSchemeUnderTheProviderName()
    {
        // The /visuauth/external-login/start page challenges the scheme
        // that GetProvidersAsync returns. If the DI extension wires OIDC
        // under a different scheme, the button POST would fail with
        // "no authentication handler registered for the scheme '…'".
        var services = BaseServices();
        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });

        using var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = await schemeProvider.GetAllSchemesAsync();
        schemes.Should().Contain(s => s.Name == EntraExternalLoginFlow.ProviderScheme,
            "the scheme name must match GetProvidersAsync().Scheme exactly — otherwise the button POST 500s");
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_DecoratesAuthenticationFlow_SoLogoutClearsTheCookie()
    {
        // The CRUD adapter's EntraExternalAuthenticationFlow.SignOutAsync is
        // a no-op (no HttpContext in that package), so /visuauth/logout
        // wouldn't actually sign the user out in External mode. The sign-in
        // package must Replace IAuthenticationFlow with the decorator that
        // clears the cookie. Pin that here so a refactor can't silently
        // regress logout.
        var services = BaseServices();
        services.AddScoped<IAuthenticationFlow>(_ => Mock.Of<IAuthenticationFlow>());
        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthenticationFlow>()
            .Should().BeOfType<EntraExternalWebAuthenticationFlow>(
                "logout has to clear the OIDC cookie — the decorator is what makes /visuauth/logout work in External mode");
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_RegistersProfileSync()
    {
        // EntraExternalLoginFlow takes IEntraExternalProfileSync — the DI
        // extension must register it or scope resolution of the flow fails.
        var services = BaseServices();
        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetService<IEntraExternalProfileSync>()
            .Should().BeOfType<EntraExternalProfileSync>();
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_RegistersHttpContextAccessor()
    {
        // EntraExternalLoginFlow reads the authenticated principal off
        // IHttpContextAccessor.HttpContext.User — if the accessor isn't
        // in DI, scope resolution throws. Sample apps that don't use
        // AddControllersWithViews / AddRazorPages won't get it for free.
        var services = BaseServices();
        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });

        using var sp = services.BuildServiceProvider();
        sp.GetService<IHttpContextAccessor>()
            .Should().NotBeNull("the flow depends on it; the registration must opt us in");
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_PostConfiguresOidc_WithCookieSignInSchemeAndNameClaimType()
    {
        // The post-configure pinning is what makes HttpContext.User.Identity.Name
        // read the friendly "name" claim, and ensures the OIDC handler
        // signs into the Cookies scheme (where EntraExternalLoginFlow reads
        // the principal from). Locking it in prevents a silent regression.
        var services = BaseServices();
        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });

        using var sp = services.BuildServiceProvider();
        var optsMonitor = sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var oidc = optsMonitor.Get(EntraExternalLoginFlow.ProviderScheme);
        oidc.SignInScheme.Should().Be("Cookies");
        oidc.TokenValidationParameters.NameClaimType.Should().Be("name");
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_PinsAuthorizationCodeFlow_NotHybridImplicit()
    {
        // Regression pin for AADSTS700054 (caught in manual smoke against a
        // real External tenant): the OpenIdConnect handler's hybrid default
        // ("code id_token") fails because External app registrations don't
        // enable the implicit id_token response type by default. Forcing
        // response_type=code keeps the id_token on the back channel (token
        // endpoint exchange) — the secure, CIAM-recommended flow — and
        // works against a fresh tenant with no extra portal toggle.
        var services = BaseServices();
        services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });

        using var sp = services.BuildServiceProvider();
        var oidc = sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(EntraExternalLoginFlow.ProviderScheme);
        oidc.ResponseType.Should().Be("code",
            "External ID rejects implicit/hybrid id_token (AADSTS700054) — the handler must use pure auth-code flow");
    }

    [Fact]
    public void AddVisuAuthEntraExternalSignIn_DoesNotRequireConfiguredUserStore_AtRegistrationTime()
    {
        // Registration must succeed even when no IUserStore is in DI yet
        // (consumers might wire AddVisuAuthEntraExternalSignIn before
        // AddVisuAuthEntraExternal). Resolution does need it, but that's
        // a request-time concern.
        var services = BaseServices();
        var act = () => services.AddVisuAuthEntraExternalSignIn(o =>
        {
            o.TenantSubdomain = "contoso";
            o.TenantId = "t";
            o.ClientId = "c";
        });
        act.Should().NotThrow();
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        // Microsoft.Identity.Web 4.10's SetIdentityModelLogger reads
        // IConfiguration off DI when building OpenIdConnectOptions. A real
        // host has it for free via WebApplication.CreateBuilder; this test
        // surface doesn't, so an empty config keeps the OIDC options
        // factory happy without polluting the assertion surface.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The flow needs an IUserStore to resolve at request time. Mock it
        // with a benign capability bag so EntraExternalLoginFlow's overlay
        // ctor logic doesn't throw on null.
        services.AddSingleton(Mock.Of<IUserStore>(s =>
            s.Capabilities == new UserBackendCapabilities()));
        // The flow also transitively needs a GraphServiceClient (via
        // IEntraExternalProfileSync) — the real AddVisuAuthEntraExternal
        // registers it; here an offline client (a credential we never call)
        // is enough for DI resolution.
        services.AddSingleton(BuildOfflineGraphClient());
        return services;
    }

    private static Microsoft.Graph.GraphServiceClient BuildOfflineGraphClient()
    {
        Azure.Core.TokenCredential offline = new Azure.Identity.ClientSecretCredential("tenant", "client", "secret");
        return new Microsoft.Graph.GraphServiceClient(offline);
    }

    private static void AssertSignInSurfaceRegistered(IServiceCollection services)
    {
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IExternalLoginFlow>()
            .Should().BeOfType<EntraExternalLoginFlow>();
    }
}
