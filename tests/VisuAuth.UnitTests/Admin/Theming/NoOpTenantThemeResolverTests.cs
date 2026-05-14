using FluentAssertions;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.UnitTests.Admin.Theming;

/// <summary>
/// The default <see cref="ITenantThemeResolver"/> wired by
/// <c>AddVisuAuthAdminUi</c> must always return <see langword="null"/>
/// — that's the contract the tag helper relies on for the
/// "no per-tenant override" fast path.
/// </summary>
public sealed class NoOpTenantThemeResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("acme")]
    public async Task ResolveAsync_ForAnyTenantId_ReturnsNull(string? tenantId)
    {
        var resolver = new NoOpTenantThemeResolver();

        var result = await resolver.ResolveAsync(tenantId);

        result.Should().BeNull(
            "the no-op default must never inject a per-tenant theme — that's the whole point of the fast path");
    }

    [Fact]
    public async Task ResolveAsync_WithCancelledToken_StillReturnsNullSynchronously()
    {
        // No I/O happens, so cancellation is a no-op. Lock that in so a
        // future change doesn't accidentally introduce an OperationCanceled
        // throw and break consumers who pass a real token.
        var resolver = new NoOpTenantThemeResolver();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await resolver.ResolveAsync("acme", cts.Token);

        result.Should().BeNull();
    }
}
