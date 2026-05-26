using FluentAssertions;
using VisuAuth.EntraCore.Infrastructure;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// Pins the synchronous defences on <see cref="EntraGraphClientFactory"/>.
/// The factory's actual Graph construction is exercised end-to-end by
/// every test that boots the adapter through DI (the singleton it
/// returns is the same one every store gets), so direct tests here only
/// need to cover the null / blank input branches.
/// </summary>
public sealed class EntraGraphClientFactoryTests
{
    [Theory]
    [InlineData(null, "c", "s")]
    [InlineData("", "c", "s")]
    [InlineData("   ", "c", "s")]
    [InlineData("t", null, "s")]
    [InlineData("t", "", "s")]
    [InlineData("t", "c", null)]
    [InlineData("t", "c", "")]
    public void Create_BlankInput_Throws(string? tenantId, string? clientId, string? clientSecret)
    {
        var act = () => EntraGraphClientFactory.Create(tenantId!, clientId!, clientSecret!);
        act.Should().Throw<ArgumentException>(
            "every credential field is required — a missing one would let the Graph call fail later with a less actionable error");
    }

    [Fact]
    public void Create_HappyPath_ReturnsConfiguredClient()
    {
        // GraphServiceClient construction never touches the network; the
        // ClientSecretCredential it wraps caches tokens lazily on the
        // first request. So this is a safe in-memory smoke.
        var client = EntraGraphClientFactory.Create(
            tenantId: "00000000-0000-0000-0000-000000000001",
            clientId: "00000000-0000-0000-0000-000000000002",
            clientSecret: "fake-secret");

        client.Should().NotBeNull();
        client.RequestAdapter.Should().NotBeNull(
            "every Graph call walks through the request adapter — a null one would crash on first use");
    }
}
