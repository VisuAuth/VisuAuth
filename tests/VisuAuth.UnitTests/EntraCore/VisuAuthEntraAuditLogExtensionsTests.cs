using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.EntraCore.Auditing;
using VisuAuth.EntraCore.DependencyInjection;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// Verifies <see cref="VisuAuthEntraAuditLogExtensions.AddVisuAuthEntraSignInAuditLog"/>
/// registers the Entra sign-in reader as <see cref="IAuditReader"/> — that
/// registration is what flips the admin audit-log page from its "not
/// enabled" state to live Entra data.
/// </summary>
public sealed class VisuAuthEntraAuditLogExtensionsTests
{
    [Fact]
    public void AddVisuAuthEntraSignInAuditLog_RegistersTheReader()
    {
        var services = BaseServices();
        services.AddVisuAuthEntraSignInAuditLog();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetService<IAuditReader>()
            .Should().BeOfType<EntraSignInAuditReader>();
    }

    [Fact]
    public void AddVisuAuthEntraSignInAuditLog_TryAdd_KeepsAPreRegisteredReader()
    {
        // A consumer who ALSO wired an EF-backed reader (hybrid deployment)
        // keeps it — TryAdd is first-wins.
        var services = BaseServices();
        var custom = Mock4Reader();
        services.AddScoped(_ => custom);
        services.AddVisuAuthEntraSignInAuditLog();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditReader>().Should().BeSameAs(custom);
    }

    private static IAuditReader Mock4Reader()
        => Moq.Mock.Of<IAuditReader>();

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        // The reader resolves a GraphServiceClient (the adapter registers
        // it for real); an offline client is enough for DI resolution.
        TokenCredential offline = new ClientSecretCredential("t", "c", "s");
        services.AddSingleton(new GraphServiceClient(offline));
        return services;
    }
}
