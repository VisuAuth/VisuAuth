using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Configuration;
using VisuAuth.Entra.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.Entra.Configuration;

/// <summary>
/// Covers the DB overlay that lets admin-edited values win over the
/// code/appsettings-bound <see cref="EntraOptions"/>, plus the static snapshot
/// it captures for the admin source badges.
/// </summary>
public sealed class EntraDbConfigOverlayTests
{
    [Fact]
    public void Configure_DbOverride_WinsOverStaticValue()
    {
        var overlay = BuildOverlay(new Dictionary<string, string>
        {
            [EntraAdapterConfigKeys.TenantId] = "db-tenant",
            [EntraAdapterConfigKeys.ClientSecret] = "db-secret",
        }, out _);

        var options = new EntraOptions
        {
            TenantId = "code-tenant",
            ClientId = "code-client",
            ClientSecret = "code-secret",
        };

        overlay.Configure(options);

        options.TenantId.Should().Be("db-tenant", "a DB override wins");
        options.ClientSecret.Should().Be("db-secret");
        options.ClientId.Should().Be("code-client", "keys without a DB override keep the code value");
    }

    [Fact]
    public void Configure_NoStoreRegistered_LeavesStaticConfigUntouched()
    {
        // Empty service provider — no IAdapterConfigStore.
        var snapshot = new EntraConfigStaticSnapshot();
        var overlay = new EntraDbConfigOverlay(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            snapshot);

        var options = new EntraOptions { TenantId = "code-tenant", ClientId = "c", ClientSecret = "s" };
        overlay.Configure(options);

        options.TenantId.Should().Be("code-tenant", "with no store the adapter keeps its code config");
    }

    [Fact]
    public void Configure_CapturesStaticSnapshot_BeforeOverlay()
    {
        var overlay = BuildOverlay(new Dictionary<string, string>
        {
            [EntraAdapterConfigKeys.TenantId] = "db-tenant",
        }, out var snapshot);

        overlay.Configure(new EntraOptions
        {
            TenantId = "code-tenant",
            ClientId = "code-client",
            ClientSecret = "code-secret",
        });

        // The snapshot reflects the pre-overlay (code) values, not the DB ones.
        snapshot.HasValue(EntraAdapterConfigKeys.TenantId).Should().BeTrue();
        snapshot.GetValue(EntraAdapterConfigKeys.TenantId).Should().Be("code-tenant");
        // ClientSecret presence is recorded, but its value is never exposed.
        snapshot.HasValue(EntraAdapterConfigKeys.ClientSecret).Should().BeTrue();
        snapshot.GetValue(EntraAdapterConfigKeys.ClientSecret).Should().BeNull();
    }

    private static EntraDbConfigOverlay BuildOverlay(
        Dictionary<string, string> dbValues,
        out EntraConfigStaticSnapshot snapshot)
    {
        snapshot = new EntraConfigStaticSnapshot();
        var services = new ServiceCollection();
        services.AddScoped<IAdapterConfigStore>(_ => new FakeAdapterConfigStore(dbValues));
        var provider = services.BuildServiceProvider();
        return new EntraDbConfigOverlay(provider.GetRequiredService<IServiceScopeFactory>(), snapshot);
    }

    private sealed class FakeAdapterConfigStore(IReadOnlyDictionary<string, string> values) : IAdapterConfigStore
    {
        public Task<IReadOnlyDictionary<string, string>> GetResolvedAsync(string adapter, CancellationToken cancellationToken = default)
            => Task.FromResult(values);

        public Task<IReadOnlyList<AdapterConfigEntryView>> ListAsync(string adapter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AdapterConfigEntryView>>([]);

        public Task<UserResult> SaveAsync(SaveAdapterConfigCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(UserResult.Success());
    }
}
