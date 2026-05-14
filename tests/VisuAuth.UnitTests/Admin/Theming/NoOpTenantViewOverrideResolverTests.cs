using FluentAssertions;
using VisuAuth.AdminUi.Theming;
using Xunit;

namespace VisuAuth.UnitTests.Admin.Theming;

/// <summary>
/// The default <see cref="ITenantViewOverrideResolver"/> must always
/// return <see langword="null"/> — that's the contract the expander
/// relies on to skip the per-tenant slot in the layer-3-only fast path.
/// </summary>
public sealed class NoOpTenantViewOverrideResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("acme")]
    public void ResolveOverrideRoot_ForAnyTenantId_ReturnsNull(string? tenantId)
    {
        var resolver = new NoOpTenantViewOverrideResolver();

        var result = resolver.ResolveOverrideRoot(tenantId);

        result.Should().BeNull(
            "the no-op default must never inject a per-tenant override path");
    }
}
