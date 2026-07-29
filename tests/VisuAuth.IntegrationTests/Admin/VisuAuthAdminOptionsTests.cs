using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.AdminUi.DependencyInjection;
using VisuAuth.AdminUi.Localization;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Pins how the admin gate resolves: the secure default, the
/// <see cref="VisuAuthAdminOptions"/> sugar, and the precedence between them and
/// a consumer-registered policy of the same name.
/// </summary>
public sealed class VisuAuthAdminOptionsTests
{
    [Fact]
    public async Task AdminPolicy_WithNoConfiguration_RequiresAnAuthenticatedUser()
    {
        var policy = await ResolvePolicyAsync(services => services.AddVisuAuthAdminUi());

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle()
            .Which.Should().BeOfType<DenyAnonymousAuthorizationRequirement>(
                "the secure default is 'an authenticated user' and nothing more");
    }

    [Fact]
    public async Task AdminPolicy_WithRequireRole_AddsARoleRequirementOnTopOfAuthentication()
    {
        var policy = await ResolvePolicyAsync(services =>
            services.AddVisuAuthAdminUi(admin => admin.RequireRole("Admin")));

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainItemsAssignableTo<DenyAnonymousAuthorizationRequirement>();
        policy.Requirements.OfType<RolesAuthorizationRequirement>().Should().ContainSingle()
            .Which.AllowedRoles.Should().BeEquivalentTo(["Admin"]);
    }

    [Fact]
    public async Task AdminPolicy_WithConfigurePolicy_UsesExactlyWhatWasConfigured()
    {
        var policy = await ResolvePolicyAsync(services =>
            services.AddVisuAuthAdminUi(admin => admin.ConfigurePolicy(p =>
                p.RequireAuthenticatedUser().RequireClaim("department", "it"))));

        policy.Should().NotBeNull();
        policy!.Requirements.OfType<ClaimsAuthorizationRequirement>().Should().ContainSingle()
            .Which.ClaimType.Should().Be("department");
    }

    [Fact]
    public async Task AdminPolicy_WhenTheConsumerRegistersTheNamedPolicy_TheirPolicyWins()
    {
        // The most explicit thing the consumer can do must never be overwritten,
        // even when they also passed options.
        var policy = await ResolvePolicyAsync(services =>
        {
            services.AddAuthorizationBuilder()
                .AddPolicy(VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy,
                    p => p.RequireAuthenticatedUser().RequireRole("HandRolled"));
            services.AddVisuAuthAdminUi(admin => admin.RequireRole("FromOptions"));
        });

        policy!.Requirements.OfType<RolesAuthorizationRequirement>().Should().ContainSingle()
            .Which.AllowedRoles.Should().BeEquivalentTo(["HandRolled"]);
    }

    [Fact]
    public async Task AdminPolicy_WithAnonymousOptOut_NoLongerDeniesAnonymous()
    {
        var policy = await ResolvePolicyAsync(services =>
        {
            services.AddVisuAuthAdminUi(admin => admin.RequireRole("Admin"));
            services.AllowAnonymousVisuAuthAdmin();
        });

        policy!.Requirements.Should().NotContainItemsAssignableTo<DenyAnonymousAuthorizationRequirement>(
            "the opt-out is the last word for consumers fronting the dashboard themselves");
    }

    [Fact]
    public void RequireRole_WithNoRoles_ThrowsRatherThanLockingEveryoneOut()
    {
        var act = () => new VisuAuthAdminOptions().RequireRole();

        act.Should().Throw<ArgumentException>(
            "an empty role list would deny every user, including administrators");
    }

    private static async Task<AuthorizationPolicy?> ResolvePolicyAsync(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVisuAuthLocalization();
        configure(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        // Touch the policy provider so PostConfigure callbacks have all run.
        await Task.Yield();
        return options.GetPolicy(VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy);
    }
}
