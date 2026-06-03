using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.EntraExternal;
using VisuAuth.EntraExternal.Web;
using Xunit;
using FluentAssertions;

namespace VisuAuth.UnitTests.EntraExternal.Web;

/// <summary>
/// Pins the one behaviour the decorator exists for: making
/// <see cref="EntraExternalWebAuthenticationFlow.SignOutAsync"/> clear the
/// OIDC session cookie (the CRUD adapter's inner flow can't, having no
/// HttpContext). Everything else must delegate straight through.
/// </summary>
public sealed class EntraExternalWebAuthenticationFlowTests
{
    [Fact]
    public void Ctor_NullInner_Throws()
    {
        var act = () => new EntraExternalWebAuthenticationFlow(null!, Mock.Of<IHttpContextAccessor>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("inner");
    }

    [Fact]
    public void Ctor_NullHttpContextAccessor_Throws()
    {
        var act = () => new EntraExternalWebAuthenticationFlow(new EntraExternalAuthenticationFlow(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpContextAccessor");
    }

    [Fact]
    public async Task SignOutAsync_ClearsTheCookieScheme()
    {
        // HttpContext.SignOutAsync(scheme) resolves IAuthenticationService
        // from RequestServices and calls SignOutAsync(context, scheme, ...).
        // Mock that service and assert the Cookies scheme is what gets
        // cleared — that's the cookie Microsoft.Identity.Web issued.
        var authService = new Mock<IAuthenticationService>();
        authService
            .Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(authService.Object);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(http);

        var sut = new EntraExternalWebAuthenticationFlow(new EntraExternalAuthenticationFlow(), accessor.Object);

        await sut.SignOutAsync(CancellationToken.None);

        authService.Verify(
            a => a.SignOutAsync(http, "Cookies", It.IsAny<AuthenticationProperties>()),
            Times.Once,
            "logout must drop the OIDC session cookie the Cookies scheme holds — otherwise the user stays signed in");
    }

    [Fact]
    public async Task SignOutAsync_NoHttpContext_DoesNotThrow()
    {
        // Outside a request (e.g. a background caller) there's no
        // HttpContext. The decorator must no-op gracefully rather than NRE.
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);

        var sut = new EntraExternalWebAuthenticationFlow(new EntraExternalAuthenticationFlow(), accessor.Object);

        var act = () => sut.SignOutAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Capabilities_DelegatesToInner()
    {
        var inner = new Mock<IAuthenticationFlow>();
        var caps = new UserBackendCapabilities { SupportsLocalLogin = false };
        inner.SetupGet(f => f.Capabilities).Returns(caps);

        var sut = new EntraExternalWebAuthenticationFlow(inner.Object, Mock.Of<IHttpContextAccessor>());

        sut.Capabilities.Should().BeSameAs(caps, "the decorator must not invent its own capability bag");
    }

    [Fact]
    public async Task SignInWithPasswordAsync_DelegatesToInner()
    {
        // The password-form shim behaviour (return RedirectToExternalProvider)
        // must be untouched — only SignOut is overridden.
        var inner = new Mock<IAuthenticationFlow>();
        var expected = new SignInResult { Outcome = SignInOutcome.RedirectToExternalProvider };
        inner.Setup(f => f.SignInWithPasswordAsync("a@b.com", "pwd", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = new EntraExternalWebAuthenticationFlow(inner.Object, Mock.Of<IHttpContextAccessor>());

        var result = await sut.SignInWithPasswordAsync("a@b.com", "pwd", persistent: true, CancellationToken.None);

        result.Should().BeSameAs(expected);
        inner.Verify(f => f.SignInWithPasswordAsync("a@b.com", "pwd", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterRequestResetConfirm_AllDelegateToInner()
    {
        var inner = new Mock<IAuthenticationFlow>();
        inner.Setup(f => f.RegisterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisuAuth.Abstractions.Common.StoreResult.Failure("x"));
        inner.Setup(f => f.RequestPasswordResetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisuAuth.Abstractions.Common.StoreResult.Failure("x"));
        inner.Setup(f => f.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisuAuth.Abstractions.Common.StoreResult.Failure("x"));
        inner.Setup(f => f.ConfirmEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisuAuth.Abstractions.Common.StoreResult.Failure("x"));

        var sut = new EntraExternalWebAuthenticationFlow(inner.Object, Mock.Of<IHttpContextAccessor>());

        await sut.RegisterAsync("a@b.com", "pwd", null, CancellationToken.None);
        await sut.RequestPasswordResetAsync("a@b.com", CancellationToken.None);
        await sut.ResetPasswordAsync("a@b.com", "tk", "new", CancellationToken.None);
        await sut.ConfirmEmailAsync("u-1", "tk", CancellationToken.None);

        inner.Verify(f => f.RegisterAsync("a@b.com", "pwd", null, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(f => f.RequestPasswordResetAsync("a@b.com", It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(f => f.ResetPasswordAsync("a@b.com", "tk", "new", It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(f => f.ConfirmEmailAsync("u-1", "tk", It.IsAny<CancellationToken>()), Times.Once);
    }
}
