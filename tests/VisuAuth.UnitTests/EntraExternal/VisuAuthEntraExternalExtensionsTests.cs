using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;
using VisuAuth.EntraExternal;
using VisuAuth.EntraExternal.Configuration;
using VisuAuth.EntraExternal.DependencyInjection;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal;

/// <summary>
/// Verifies that
/// <see cref="VisuAuthEntraExternalExtensions.AddVisuAuthEntraExternal(IServiceCollection, Action{EntraExternalOptions})"/>
/// and friends register the full External adapter surface — both
/// overloads must produce an equivalent service graph so consumers can
/// pick either without surprise.
/// </summary>
public sealed class VisuAuthEntraExternalExtensionsTests
{
    [Fact]
    public void AddVisuAuthEntraExternal_WithLambda_RegistersStoresAndAuthFlow()
    {
        var services = BaseServices();
        services.AddVisuAuthEntraExternal(o =>
        {
            o.TenantId = Guid.NewGuid().ToString();
            o.ClientId = Guid.NewGuid().ToString();
            o.ClientSecret = "secret-value";
            o.TenantDomain = "contoso.onmicrosoft.com";
        });

        AssertExternalSurfaceRegistered(services);
    }

    [Fact]
    public void AddVisuAuthEntraExternal_FromConfiguration_BindsOptionsAndRegistersStores()
    {
        var services = BaseServices();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VisuAuth:EntraExternal:TenantId"] = "tenant-1",
                ["VisuAuth:EntraExternal:ClientId"] = "client-1",
                ["VisuAuth:EntraExternal:ClientSecret"] = "secret-1",
                ["VisuAuth:EntraExternal:TenantDomain"] = "contoso.onmicrosoft.com",
                ["VisuAuth:EntraExternal:AppRoleResourceId"] = "target-app-1",
            })
            .Build();

        services.AddVisuAuthEntraExternal(config);

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<EntraExternalOptions>>().Value;
        opts.TenantId.Should().Be("tenant-1");
        opts.ClientId.Should().Be("client-1");
        opts.ClientSecret.Should().Be("secret-1");
        opts.TenantDomain.Should().Be("contoso.onmicrosoft.com");
        opts.AppRoleResourceId.Should().Be("target-app-1");
        AssertExternalSurfaceRegistered(services);
    }

    [Fact]
    public void AddVisuAuthEntraExternal_Capabilities_ExposedThroughIUserStore_MatchSingleton()
    {
        // Pull IUserStore from DI and assert its Capabilities match the
        // EntraExternalCapabilities singleton — proves the registration
        // wires the right concrete type (EntraExternalUserStore) without
        // instantiating GraphServiceClient against the network.
        var services = BaseServices();
        services.AddVisuAuthEntraExternal(o =>
        {
            o.TenantId = "t";
            o.ClientId = "c";
            o.ClientSecret = "s";
            o.TenantDomain = "contoso.onmicrosoft.com";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var userStore = scope.ServiceProvider.GetRequiredService<IUserStore>();
        userStore.Should().BeOfType<EntraExternalUserStore>();
        // Structural equality (not reference) — the store overlays the
        // EntraExternalOptions.DefaultEmailDomain onto the singleton Value
        // when computing Capabilities, so a brand-new copy comes back.
        userStore.Capabilities.Should().Be(
            EntraExternalCapabilities.Value with { EmailDomainSuffix = userStore.Capabilities.EmailDomainSuffix });
    }

    [Fact]
    public void AddVisuAuthEntraExternal_DoesNotOverridePreviouslyRegisteredAuthFlow()
    {
        // TryAdd semantics: a consumer that registers their own
        // IAuthenticationFlow BEFORE calling AddVisuAuthEntraExternal
        // (e.g. a test double) must win. This protects integration tests
        // and edge wiring like "use External stores but stub the auth
        // flow".
        var services = BaseServices();
        var custom = new EntraExternalAuthenticationFlow();
        services.AddSingleton<IAuthenticationFlow>(custom);
        services.AddVisuAuthEntraExternal(o =>
        {
            o.TenantId = "t";
            o.ClientId = "c";
            o.ClientSecret = "s";
            o.TenantDomain = "contoso.onmicrosoft.com";
        });

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IAuthenticationFlow>().Should().BeSameAs(custom,
            "TryAdd preserves the consumer's earlier registration");
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        return services;
    }

    private static void AssertExternalSurfaceRegistered(IServiceCollection services)
    {
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserStore>().Should().BeOfType<EntraExternalUserStore>();
        scope.ServiceProvider.GetRequiredService<IRoleStore>().Should().BeOfType<EntraExternalRoleStore>();
        scope.ServiceProvider.GetRequiredService<IAuthenticationFlow>().Should().BeOfType<EntraExternalAuthenticationFlow>();
    }
}
