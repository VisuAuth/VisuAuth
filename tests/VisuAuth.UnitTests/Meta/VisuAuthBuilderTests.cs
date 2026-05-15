using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Abstractions.Users;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Meta;

/// <summary>
/// Covers the fluent <see cref="IVisuAuthBuilder"/> entry point in the
/// meta-package and asserts that the one-liner
/// <c>AddVisuAuth&lt;TUser&gt;()</c> still produces an equivalent service
/// graph — the drop-in promise from CLAUDE.md §2.1.
/// </summary>
public sealed class VisuAuthBuilderTests
{
    [Fact]
    public void AddVisuAuth_OnEmptyCollection_ReturnsBuilderBackedByTheSameServices()
    {
        var services = new ServiceCollection();

        var builder = services.AddVisuAuth();

        builder.Should().NotBeNull();
        builder.Services.Should().BeSameAs(services,
            "the fluent builder must register against the caller's collection so subsequent .Add* calls take effect on the host's DI container");
    }

    [Fact]
    public void UseAspNetIdentity_OnFluentChain_RegistersUserAndRoleStores()
    {
        var services = new ServiceCollection();

        services.AddVisuAuth().UseAspNetIdentity<IdentityUser>();

        services.Should().Contain(d => d.ServiceType == typeof(IUserStore),
            "UseAspNetIdentity must register the Identity-backed IUserStore");
        services.Should().Contain(d => d.ServiceType == typeof(IRoleStore),
            "UseAspNetIdentity must register the Identity-backed IRoleStore");
        services.Should().Contain(d => d.ServiceType == typeof(IAuthenticationFlow),
            "UseAspNetIdentity must register the Identity-backed sign-in flow");
    }

    [Fact]
    public void AddAdminUi_AfterUseAspNetIdentity_LeavesAuthenticationFlowRegistered()
    {
        var services = new ServiceCollection();

        services.AddVisuAuth()
            .UseAspNetIdentity<IdentityUser>()
            .AddAdminUi()
            .AddEndUserUi();

        services.Should().Contain(d => d.ServiceType == typeof(IUserStore),
            "the chain order must not clobber upstream registrations");
        services.Should().Contain(d => d.ServiceType == typeof(IAuthenticationFlow),
            "AddAdminUi / AddEndUserUi must not overwrite the auth flow installed by UseAspNetIdentity");
    }

    [Fact]
    public void EnableMultiTenant_OnBuilder_UpgradesTenantContextToHttpAware()
    {
        var services = new ServiceCollection();

        services.AddVisuAuth()
            .UseAspNetIdentity<IdentityUser>()
            .EnableMultiTenant(o => o.HeaderName = "X-Acme-Tenant");

        // EnableMultiTenant removes the NoOpTenantContext registered by
        // UseAspNetIdentity and installs the HTTP-aware one.
        var tenantContextDescriptors = services
            .Where(d => d.ServiceType == typeof(ITenantContext))
            .ToList();
        tenantContextDescriptors.Should().HaveCount(1,
            "the no-op context must be replaced, not stacked");
        tenantContextDescriptors[0].ImplementationType.Should().Be<HttpContextTenantContext>(
            "EnableMultiTenant must upgrade the no-op context to the HTTP-aware one");
    }

    [Fact]
    public void AddVisuAuth_OneLinerGeneric_ProducesSameServiceTypesAsFluentChain()
    {
        var oneLiner = new ServiceCollection();
        oneLiner.AddVisuAuth<IdentityUser>();

        var fluent = new ServiceCollection();
        fluent.AddVisuAuth()
            .UseAspNetIdentity<IdentityUser>()
            .AddAdminUi()
            .AddEndUserUi();

        // The one-liner must be observably equivalent to the explicit chain
        // so existing drop-in consumers keep working when we ship the
        // fluent surface.
        var oneLinerTypes = oneLiner.Select(d => d.ServiceType).Distinct().OrderBy(t => t.FullName).ToList();
        var fluentTypes = fluent.Select(d => d.ServiceType).Distinct().OrderBy(t => t.FullName).ToList();
        fluentTypes.Should().BeEquivalentTo(oneLinerTypes,
            "swapping AddVisuAuth<TUser>() for the fluent chain must not change which contracts are registered");
    }
}
