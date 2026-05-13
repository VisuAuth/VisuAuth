using FluentAssertions;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Identity.MultiTenancy;

public sealed class NoOpTenantContextTests
{
    [Fact]
    public void IsMultiTenancyEnabled_DefaultInstance_ReturnsFalse()
    {
        var context = new NoOpTenantContext();

        context.IsMultiTenancyEnabled.Should().BeFalse(
            "the no-op context represents a single-tenant deployment");
    }

    [Fact]
    public void CurrentTenantId_DefaultInstance_ReturnsNull()
    {
        var context = new NoOpTenantContext();

        context.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public void CurrentTenantDisplayName_DefaultInstance_ReturnsNull()
    {
        var context = new NoOpTenantContext();

        context.CurrentTenantDisplayName.Should().BeNull();
    }
}
