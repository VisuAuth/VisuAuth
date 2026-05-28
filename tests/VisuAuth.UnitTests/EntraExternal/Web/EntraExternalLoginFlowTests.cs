using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Users;
using VisuAuth.EntraExternal.Web;
using Xunit;

namespace VisuAuth.UnitTests.EntraExternal.Web;

/// <summary>
/// Behaviour pin for <see cref="EntraExternalLoginFlow"/>. The flow is
/// the bridge between the OIDC-authenticated principal Microsoft.Identity.Web
/// leaves on <see cref="HttpContext.User"/> and the
/// <see cref="ExternalSignInResult"/> envelope the End-user
/// <c>Callback</c> page expects. Every branch through
/// <see cref="EntraExternalLoginFlow.CompleteSignInAsync"/> deserves a
/// pinned test because the surface routes back to a user-visible
/// outcome (success redirect, "no session" error, etc.).
/// </summary>
public sealed class EntraExternalLoginFlowTests
{
    [Fact]
    public void Ctor_NullHttpContextAccessor_Throws()
    {
        var act = () => new EntraExternalLoginFlow(
            null!,
            Mock.Of<IUserStore>(s => s.Capabilities == new UserBackendCapabilities()),
            NoOpProfileSync());
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpContextAccessor");
    }

    [Fact]
    public void Ctor_NullUserStore_Throws()
    {
        var act = () => new EntraExternalLoginFlow(Mock.Of<IHttpContextAccessor>(), null!, NoOpProfileSync());
        act.Should().Throw<ArgumentNullException>().WithParameterName("userStore");
    }

    [Fact]
    public void Ctor_NullProfileSync_Throws()
    {
        var act = () => new EntraExternalLoginFlow(
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserStore>(s => s.Capabilities == new UserBackendCapabilities()),
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("profileSync");
    }

    [Fact]
    public void Capabilities_OverlaysSupportsExternalProvidersTrue_OverTheUserStoreBag()
    {
        // The store's capability bag (from EntraExternalCapabilities) ships
        // SupportsExternalProviders = false because the CRUD-only adapter has
        // no providers to list. Once this package is wired, we DO have one —
        // the overlay flips it true so /admin sidebar etc. can render the
        // providers section accordingly.
        var storeCaps = new UserBackendCapabilities
        {
            SupportsExternalProviders = false,
            SupportsLocalLogin = false,
            SupportsRoleManagement = true,
        };
        var sut = new EntraExternalLoginFlow(Mock.Of<IHttpContextAccessor>(), MockStore(storeCaps), NoOpProfileSync());

        sut.Capabilities.SupportsExternalProviders.Should().BeTrue(
            "wiring the sign-in package proves we have a real provider to surface");
        sut.Capabilities.SupportsLocalLogin.Should().BeFalse(
            "the rest of the bag flows through unchanged");
        sut.Capabilities.SupportsRoleManagement.Should().BeTrue();
    }

    [Fact]
    public async Task GetProvidersAsync_ReturnsSingleMicrosoftEntry()
    {
        var sut = BuildFlow(authenticated: false);

        var providers = await sut.GetProvidersAsync(CancellationToken.None);

        providers.Should().ContainSingle();
        providers[0].Scheme.Should().Be(EntraExternalLoginFlow.ProviderScheme,
            "the scheme must match what AddVisuAuthEntraExternalSignIn registers — see ProviderScheme const");
        providers[0].DisplayName.Should().Be(EntraExternalLoginFlow.ProviderDisplayName,
            "the display name is what end users read on the button");
    }

    [Fact]
    public async Task CompleteSignInAsync_NoAuthenticatedPrincipal_ReturnsNoExternalSession()
    {
        // No HttpContext.User.Identity.IsAuthenticated → the OIDC cookie
        // didn't land. Surface the same outcome the Callback page treats
        // as a clean "external session missing" failure.
        var sut = BuildFlow(authenticated: false);

        var result = await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.NoExternalSession);
    }

    [Fact]
    public async Task CompleteSignInAsync_AuthenticatedButNoObjectId_ReturnsFailedWithExplanation()
    {
        // Authenticated but the principal carries no `oid` claim — this
        // happens when the OIDC scopes don't include the openid scope or
        // the consumer overrode the claim mapping. The result must point
        // operators at the actionable fix (OIDC scopes).
        var principal = AuthenticatedPrincipal(("name", "Alice"));
        var sut = BuildFlow(principal);

        var result = await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().ContainMatch("*object identifier*");
    }

    [Fact]
    public async Task CompleteSignInAsync_HappyPath_UserExistsInDirectory_ReturnsSuccessWithGraphId()
    {
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal(("oid", oid), ("name", "Alice"));
        var store = new Mock<IUserStore>();
        store.SetupGet(s => s.Capabilities).Returns(new UserBackendCapabilities());
        store.Setup(s => s.GetAsync(oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSummary { Id = oid, Email = "alice@example.com" });
        var sut = new EntraExternalLoginFlow(BuildHttpContextAccessor(principal), store.Object, NoOpProfileSync());

        var result = await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.Success);
        result.UserId.Should().Be(oid,
            "downstream audit + redirect logic uses the graph object id as the canonical user identifier");
    }

    [Fact]
    public async Task CompleteSignInAsync_HappyPath_InvokesProfileSync_WithThePrincipalAndObjectId()
    {
        // The profile-sync step (PR D) runs on successful sign-in so
        // sign-up-flow-collected attributes land on the Graph user. Pin
        // that it's called with the authenticated principal + the resolved
        // object id, after the user is verified.
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal(("oid", oid), ("given_name", "Alice"));
        var store = new Mock<IUserStore>();
        store.SetupGet(s => s.Capabilities).Returns(new UserBackendCapabilities());
        store.Setup(s => s.GetAsync(oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSummary { Id = oid, Email = "alice@example.com" });
        var profileSync = new Mock<IEntraExternalProfileSync>();
        profileSync.Setup(s => s.SyncAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = new EntraExternalLoginFlow(BuildHttpContextAccessor(principal), store.Object, profileSync.Object);

        var result = await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.Success);
        profileSync.Verify(s => s.SyncAsync(principal, oid, It.IsAny<CancellationToken>()), Times.Once,
            "successful sign-in must trigger the claims→Graph profile sync");
    }

    [Fact]
    public async Task CompleteSignInAsync_UserMissing_DoesNotInvokeProfileSync()
    {
        // No verified user → no profile to sync. The sync must not run on
        // the failure path (there's no directory user to PATCH).
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal(("oid", oid));
        var store = new Mock<IUserStore>();
        store.SetupGet(s => s.Capabilities).Returns(new UserBackendCapabilities());
        store.Setup(s => s.GetAsync(oid, It.IsAny<CancellationToken>())).ReturnsAsync((UserSummary?)null);
        var profileSync = new Mock<IEntraExternalProfileSync>();
        var sut = new EntraExternalLoginFlow(BuildHttpContextAccessor(principal), store.Object, profileSync.Object);

        await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        profileSync.Verify(s => s.SyncAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteSignInAsync_HappyPath_UsesLongUriObjectIdClaim()
    {
        // Microsoft.Identity.Web's DEFAULT claim mapper emits the long URI
        // form for the object identifier. The flow has to honour that as
        // well as the short "oid" alias.
        const string longClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal((longClaim, oid));
        var store = new Mock<IUserStore>();
        store.SetupGet(s => s.Capabilities).Returns(new UserBackendCapabilities());
        store.Setup(s => s.GetAsync(oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSummary { Id = oid, Email = "alice@example.com" });
        var sut = new EntraExternalLoginFlow(BuildHttpContextAccessor(principal), store.Object, NoOpProfileSync());

        var result = await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.Success);
        result.UserId.Should().Be(oid);
    }

    [Fact]
    public async Task CompleteSignInAsync_UserMissingInDirectory_AutoCreate_ReturnsFailedWithDirectoryHint()
    {
        // AutoCreate is the supported strategy. A missing user means the
        // hosted signup flow hasn't propagated yet (or never ran). Failure
        // with an actionable message is the right surface — we deliberately
        // do NOT call IUserStore.CreateAsync because Microsoft owns directory
        // creation in External tenants.
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal(("oid", oid));
        var store = new Mock<IUserStore>();
        store.SetupGet(s => s.Capabilities).Returns(new UserBackendCapabilities());
        store.Setup(s => s.GetAsync(oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserSummary?)null);
        var sut = new EntraExternalLoginFlow(BuildHttpContextAccessor(principal), store.Object, NoOpProfileSync());

        var result = await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().ContainMatch("*directory*");
        store.Verify(s => s.CreateAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "External directory creation is owned by Microsoft's hosted user flow; we must NOT try to fall back to IUserStore.CreateAsync");
    }

    [Theory]
    [InlineData(ExternalLoginFirstTimeStrategy.AlwaysConfirm)]
    [InlineData(ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm)]
    public async Task CompleteSignInAsync_UserMissingInDirectory_ConfirmStrategies_FailWithFlowOwnershipHint(
        ExternalLoginFirstTimeStrategy strategy)
    {
        // Confirmation pages can't create users in External — surfacing
        // them would lead the user into a form that calls IUserStore.CreateAsync,
        // which the External adapter can't honour without the hosted user
        // flow context. Graceful failure with a clear hint instead.
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal(("oid", oid));
        var store = new Mock<IUserStore>();
        store.SetupGet(s => s.Capabilities).Returns(new UserBackendCapabilities());
        store.Setup(s => s.GetAsync(oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserSummary?)null);
        var sut = new EntraExternalLoginFlow(BuildHttpContextAccessor(principal), store.Object, NoOpProfileSync());

        var result = await sut.CompleteSignInAsync(strategy);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().ContainMatch("*user flow*",
            "the hint must point operators at the right place to fix this (the hosted user flow), not at VisuAuth");
    }

    [Fact]
    public async Task ConfirmAndCreateAsync_AlwaysReturnsFailedWithFlowOwnershipHint()
    {
        // Even if a stale URL leads someone to /visuauth/external-login/confirm
        // and they submit, surface a clean failure rather than crashing.
        var sut = BuildFlow(authenticated: false);

        var result = await sut.ConfirmAndCreateAsync("a@b.com", "Alice", tenantId: null);

        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().ContainMatch("*sign-up*");
    }

    [Fact]
    public async Task GetPendingInfoAsync_NoAuthenticatedPrincipal_ReturnsNull()
    {
        var sut = BuildFlow(authenticated: false);
        (await sut.GetPendingInfoAsync()).Should().BeNull(
            "no principal = nothing to render on the confirm page; the contract is to return null, not throw");
    }

    [Fact]
    public async Task GetPendingInfoAsync_AuthenticatedNoOid_ReturnsNull()
    {
        // Same guard as CompleteSignInAsync — if the token didn't carry an
        // oid claim, we can't surface a meaningful ProviderKey for the
        // confirm page.
        var principal = AuthenticatedPrincipal(("name", "Alice"));
        var sut = BuildFlow(principal);
        (await sut.GetPendingInfoAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetPendingInfoAsync_AuthenticatedWithOid_ReturnsPendingInfoFromClaims()
    {
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal(
            ("oid", oid),
            ("name", "Alice"),
            (ClaimTypes.Email, "alice@example.com"));
        var sut = BuildFlow(principal);

        var info = await sut.GetPendingInfoAsync();

        info.Should().NotBeNull();
        info!.Provider.Should().Be(EntraExternalLoginFlow.ProviderScheme);
        info.ProviderKey.Should().Be(oid);
        info.Email.Should().Be("alice@example.com");
        info.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task GetPendingInfoAsync_FallsBackToPreferredUsername_WhenEmailClaimMissing()
    {
        // OIDC tokens often omit the email claim and only carry
        // preferred_username — the flow should fall back so the confirm
        // page renders something the user can verify.
        var oid = Guid.NewGuid().ToString();
        var principal = AuthenticatedPrincipal(
            ("oid", oid),
            ("preferred_username", "alice@example.com"));
        var sut = BuildFlow(principal);

        (await sut.GetPendingInfoAsync())!.Email.Should().Be("alice@example.com");
    }

    // ---- helpers --------------------------------------------------------

    private static EntraExternalLoginFlow BuildFlow(bool authenticated)
    {
        var principal = authenticated
            ? AuthenticatedPrincipal(("oid", Guid.NewGuid().ToString()))
            : new ClaimsPrincipal(new ClaimsIdentity());
        return BuildFlow(principal);
    }

    private static EntraExternalLoginFlow BuildFlow(ClaimsPrincipal principal)
        => new(BuildHttpContextAccessor(principal),
               MockStore(new UserBackendCapabilities()),
               NoOpProfileSync());

    private static IEntraExternalProfileSync NoOpProfileSync()
    {
        var sync = new Mock<IEntraExternalProfileSync>();
        sync.Setup(s => s.SyncAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sync.Object;
    }

    private static IUserStore MockStore(UserBackendCapabilities caps)
    {
        var store = new Mock<IUserStore>();
        store.SetupGet(s => s.Capabilities).Returns(caps);
        return store.Object;
    }

    private static IHttpContextAccessor BuildHttpContextAccessor(ClaimsPrincipal principal)
    {
        var http = new DefaultHttpContext { User = principal };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(http);
        return accessor.Object;
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "TestOidc");
        return new ClaimsPrincipal(identity);
    }
}
