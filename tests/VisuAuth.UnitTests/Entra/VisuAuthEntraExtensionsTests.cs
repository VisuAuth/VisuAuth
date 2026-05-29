using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Configuration;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;
using VisuAuth.Entra;
using VisuAuth.Entra.Configuration;
using VisuAuth.Entra.DependencyInjection;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Verifies that <see cref="VisuAuthEntraExtensions.AddVisuAuthEntra(IServiceCollection, Action{EntraOptions})"/>
/// and friends register the full Entra adapter surface — both overloads
/// must produce an equivalent service graph so consumers can pick either
/// without surprise.
/// </summary>
public sealed class VisuAuthEntraExtensionsTests
{
    [Fact]
    public void AddVisuAuthEntra_WithLambda_RegistersStoresAndAuthFlow()
    {
        var services = BaseServices();
        services.AddVisuAuthEntra(o =>
        {
            o.TenantId = Guid.NewGuid().ToString();
            o.ClientId = Guid.NewGuid().ToString();
            o.ClientSecret = "secret-value";
        });

        AssertEntraSurfaceRegistered(services);
    }

    [Fact]
    public void AddVisuAuthEntra_FromConfiguration_BindsOptionsAndRegistersStores()
    {
        var services = BaseServices();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VisuAuth:Entra:TenantId"] = "tenant-1",
                ["VisuAuth:Entra:ClientId"] = "client-1",
                ["VisuAuth:Entra:ClientSecret"] = "secret-1",
                ["VisuAuth:Entra:AppRoleResourceId"] = "target-app-1",
            })
            .Build();

        services.AddVisuAuthEntra(config);

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<EntraOptions>>().Value;
        opts.TenantId.Should().Be("tenant-1");
        opts.ClientId.Should().Be("client-1");
        opts.ClientSecret.Should().Be("secret-1");
        opts.AppRoleResourceId.Should().Be("target-app-1");
        AssertEntraSurfaceRegistered(services);
    }

    [Fact]
    public void AddVisuAuthEntra_Capabilities_ExposedThroughIUserStore_MatchSingleton()
    {
        // Pull IUserStore from DI and assert its Capabilities match the
        // EntraCapabilities singleton — proves the registration wires the
        // right concrete type (EntraUserStore) without instantiating
        // GraphServiceClient (we don't need a real one for capability read).
        var services = BaseServices();
        services.AddVisuAuthEntra(o =>
        {
            o.TenantId = "t";
            o.ClientId = "c";
            o.ClientSecret = "s";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var userStore = scope.ServiceProvider.GetRequiredService<IUserStore>();
        userStore.Should().BeOfType<EntraUserStore>();
        // Structural equality (not reference) — the store overlays the
        // EntraOptions.DefaultEmailDomain onto the singleton Value when
        // computing Capabilities, so a brand-new copy comes back. Every
        // flag should still mirror the static set.
        userStore.Capabilities.Should().Be(
            EntraCapabilities.Value with { EmailDomainSuffix = userStore.Capabilities.EmailDomainSuffix });
    }

    [Fact]
    public void AddVisuAuthEntra_DoesNotOverridePreviouslyRegisteredAuthFlow()
    {
        // TryAdd semantics: a consumer that registers their own
        // IAuthenticationFlow BEFORE calling AddVisuAuthEntra (e.g. a
        // test double) must win. This protects integration tests + edge
        // wiring like "use Entra stores but stub the auth flow".
        var services = BaseServices();
        var custom = new EntraAuthenticationFlow();
        services.AddSingleton<IAuthenticationFlow>(custom);
        services.AddVisuAuthEntra(o =>
        {
            o.TenantId = "t";
            o.ClientId = "c";
            o.ClientSecret = "s";
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

    [Fact]
    public void AddVisuAuthEntra_RegistersEmailDomainSource()
    {
        // The multi-domain create-user dropdown resolves IEmailDomainSource
        // from DI; the Entra adapter must wire its Graph-backed implementation
        // so a multi-domain tenant gets the dropdown out of the box.
        var services = BaseServices();
        services.AddVisuAuthEntra(o =>
        {
            o.TenantId = "t";
            o.ClientId = "c";
            o.ClientSecret = "s";
        });

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IEmailDomainSource)
            && d.ImplementationType == typeof(EntraEmailDomainSource));
    }

    [Fact]
    public void AddVisuAuthEntraDbConfig_RegistersOverlaySchemaNotifierAndChangeSource()
    {
        var services = BaseServices();
        services.AddVisuAuthEntra(o =>
        {
            o.TenantId = "t";
            o.ClientId = "c";
            o.ClientSecret = "s";
        });
        services.AddVisuAuthEntraDbConfig();

        using var sp = services.BuildServiceProvider();

        sp.GetServices<IAdapterConfigSchema>().Should().ContainSingle(s => s is EntraAdapterConfigSchema);
        sp.GetServices<IAdapterConfigChangeNotifier>().Should().ContainSingle(n => n is EntraConfigChangeNotifier);
        sp.GetServices<IConfigureOptions<EntraOptions>>().Should().Contain(c => c is EntraDbConfigOverlay);
        sp.GetServices<IOptionsChangeTokenSource<EntraOptions>>().Should().Contain(c => c is EntraConfigChangeTokenSource);
        sp.GetService<EntraGraphClientProvider>().Should().NotBeNull(
            "the Graph client comes from the provider so it can rebuild on config change");
    }

    private static void AssertEntraSurfaceRegistered(IServiceCollection services)
    {
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserStore>().Should().BeOfType<EntraUserStore>();
        scope.ServiceProvider.GetRequiredService<IRoleStore>().Should().BeOfType<EntraRoleStore>();
        scope.ServiceProvider.GetRequiredService<IAuthenticationFlow>().Should().BeOfType<EntraAuthenticationFlow>();
    }
}
