using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using VisuAuth.Identity.MultiTenancy;
using Xunit;

namespace VisuAuth.UnitTests.Identity.MultiTenancy;

public sealed class HttpContextTenantContextTests
{
    [Fact]
    public void IsMultiTenancyEnabled_AlwaysReturnsTrue()
    {
        var accessor = Mock.Of<IHttpContextAccessor>();

        var context = new HttpContextTenantContext(accessor);

        context.IsMultiTenancyEnabled.Should().BeTrue(
            "the http-backed context is registered when the consumer opts in");
    }

    [Fact]
    public void CurrentTenantId_WhenHttpContextIsNull_ReturnsNull()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);

        var context = new HttpContextTenantContext(accessor.Object);

        context.CurrentTenantId.Should().BeNull(
            "background workers / seeders run without an HttpContext and must see a null scope");
    }

    [Fact]
    public void CurrentTenantId_WhenItemMissing_ReturnsNull()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var context = new HttpContextTenantContext(accessor.Object);

        context.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public void CurrentTenantId_WhenItemSet_ReturnsTheStashedValue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[HttpContextTenantContext.TenantIdItemKey] = "acme";
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var context = new HttpContextTenantContext(accessor.Object);

        context.CurrentTenantId.Should().Be("acme");
    }

    [Fact]
    public void CurrentTenantDisplayName_WhenItemSet_ReturnsTheStashedValue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[HttpContextTenantContext.TenantDisplayNameItemKey] = "Acme Corp";
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var context = new HttpContextTenantContext(accessor.Object);

        context.CurrentTenantDisplayName.Should().Be("Acme Corp");
    }
}
