using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using VisuAuth.Entra;
using VisuAuth.Entra.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Lock-down for <see cref="EntraRoleStore"/>'s NotSupported branches —
/// Create / Rename / Delete throw because app roles are declared in the
/// app manifest, not at runtime. The IUserStore contract says
/// "implementations should throw NotSupportedException when the backend
/// lacks support" (IRoleStore docs say the same), and these tests pin
/// that contract per method so a regression here can't sneak through as
/// a silent success.
/// </summary>
/// <remarks>
/// The Graph-touching branches (List / Assign / Remove / etc.) need a
/// recorded-response harness against a real tenant — gated for v0.3. For
/// the v0.2 unit suite we cover the synchronous NotSupported throws and
/// the constructor's null-arg defence.
/// </remarks>
public sealed class EntraRoleStoreTests
{
    [Fact]
    public void Ctor_NullGraphClient_Throws()
    {
        var act = () => new EntraRoleStore(null!, Options.Create(new EntraOptions()));
        act.Should().Throw<ArgumentNullException>().WithParameterName("graphClient");
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        // Graph constructor needs at least an auth provider; the cheapest
        // path is a credential we never call against the network.
        var graph = BuildOfflineGraphClient();
        var act = () => new EntraRoleStore(graph, null!);
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
        // App role ids are GUIDs in Entra; calling Get with a non-GUID
        // input must short-circuit instead of round-tripping the SDK
        // (which would crash on the Guid.Parse downstream).
        var sut = BuildStore();
        (await sut.GetAsync("not-a-guid")).Should().BeNull(
            "non-GUID role ids can never match an Entra app role — bail before the network call");
    }

    private static EntraRoleStore BuildStore()
        => new(BuildOfflineGraphClient(), Options.Create(new EntraOptions
        {
            TenantId = "t",
            ClientId = "c",
            ClientSecret = "s",
        }));

    /// <summary>
    /// Builds a GraphServiceClient with a credential that never hits the
    /// network — the tests in this file deliberately don't exercise any
    /// Graph endpoint, so the client just needs to construct without
    /// requiring real Azure connectivity.
    /// </summary>
    private static GraphServiceClient BuildOfflineGraphClient()
    {
        TokenCredential offline = new ClientSecretCredential("tenant", "client", "secret");
        return new GraphServiceClient(offline);
    }
}
