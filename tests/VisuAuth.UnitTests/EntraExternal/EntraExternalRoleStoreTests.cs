using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using VisuAuth.EntraExternal;
using VisuAuth.EntraExternal.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal;

/// <summary>
/// Lock-down for <see cref="EntraExternalRoleStore"/>'s NotSupported
/// branches — Create / Rename / Delete throw because app roles are
/// declared in the app manifest, not at runtime. Identical contract to
/// the Workforce role store; the duplication exists per the comment on
/// <see cref="EntraExternalRoleStore"/> (no shared base to avoid
/// EntraCore depending on a typed options class).
/// </summary>
public sealed class EntraExternalRoleStoreTests
{
    [Fact]
    public void Ctor_NullGraphClient_Throws()
    {
        var act = () => new EntraExternalRoleStore(null!, Options.Create(BuildOptions()));
        act.Should().Throw<ArgumentNullException>().WithParameterName("graphClient");
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var graph = BuildOfflineGraphClient();
        var act = () => new EntraExternalRoleStore(graph, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task CreateAsync_Throws_BecauseAppRolesAreManifestDeclared()
    {
        var sut = BuildStore();
        var act = () => sut.CreateAsync("role-name", tenantId: null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.WithMessage("*Entra portal*",
            "the error must point operators at where the real fix is — the Entra portal app registration");
    }

    [Fact]
    public async Task RenameAsync_Throws_NotSupported()
    {
        var sut = BuildStore();
        var act = () => sut.RenameAsync("role-id", "new-name", CancellationToken.None);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task DeleteAsync_Throws_NotSupported()
    {
        var sut = BuildStore();
        var act = () => sut.DeleteAsync("role-id", CancellationToken.None);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_BlankId_Throws(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.GetAsync(id!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "role")]
    [InlineData("", "role")]
    [InlineData("u-1", null)]
    [InlineData("u-1", "")]
    public async Task AssignRoleAsync_BlankArgs_Throws(string? userId, string? roleName)
    {
        var sut = BuildStore();
        var act = () => sut.AssignRoleAsync(userId!, roleName!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "role")]
    [InlineData("", "role")]
    [InlineData("u-1", null)]
    [InlineData("u-1", "")]
    public async Task RemoveRoleAsync_BlankArgs_Throws(string? userId, string? roleName)
    {
        var sut = BuildStore();
        var act = () => sut.RemoveRoleAsync(userId!, roleName!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetRolesForUserAsync_BlankId_Throws(string? userId)
    {
        var sut = BuildStore();
        var act = () => sut.GetRolesForUserAsync(userId!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_NonGuidId_ReturnsNull_BeforeTouchingGraph()
    {
        // Same short-circuit as the Workforce role store: non-GUID role
        // ids can never match an Entra app role — bail before the network
        // call instead of letting Guid.Parse crash downstream.
        var sut = BuildStore();
        (await sut.GetAsync("not-a-guid")).Should().BeNull(
            "non-GUID role ids are unreachable in Entra — bail before the network call");
    }

    private static EntraExternalRoleStore BuildStore()
        => new(BuildOfflineGraphClient(), Options.Create(BuildOptions()));

    private static EntraExternalOptions BuildOptions()
        => new()
        {
            TenantId = "t",
            ClientId = "c",
            ClientSecret = "s",
            TenantDomain = "contoso.onmicrosoft.com",
        };

    private static GraphServiceClient BuildOfflineGraphClient()
    {
        TokenCredential offline = new ClientSecretCredential("tenant", "client", "secret");
        return new GraphServiceClient(offline);
    }
}
