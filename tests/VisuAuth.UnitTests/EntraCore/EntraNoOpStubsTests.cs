using FluentAssertions;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.EntraCore.Stubs;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// The no-op stubs in VisuAuth.EntraCore are trivial fallbacks that keep
/// DI resolution healthy in Entra-only deployments (where Identity-adapter
/// implementations of IAuditWriter / IJwtIssuer / ITenantContext /
/// IExternalLoginFlow aren't registered). They have to do EXACTLY two
/// things: return the "this is a no-op" shape, and not throw. These
/// tests pin both.
/// </summary>
public sealed class EntraNoOpStubsTests
{
    [Fact]
    public async Task NoOpAuditWriter_WriteAsync_CompletesWithoutThrowing()
    {
        var sut = new EntraNoOpAuditWriter();
        var task = sut.WriteAsync(new AuditEvent
        {
            Action = "Test",
            TargetType = "User",
        });
        await task;
        task.IsCompletedSuccessfully.Should().BeTrue(
            "the audit writer fallback must never break the call site — it just swallows the event");
    }

    [Fact]
    public async Task NoOpJwtIssuer_IssueAsync_ReturnsNull_SoAuthApiSurfacesClean401()
    {
        var sut = new EntraNoOpJwtIssuer();
        var token = await sut.IssueAsync("u-1");
        token.Should().BeNull(
            "AuthApi.IssueOrUnauthorized treats null as 'user not eligible' and surfaces a 401 — that's the right shape for Entra mode where Microsoft owns token issuance");
    }

    [Fact]
    public void NoOpTenantContext_ReportsSingleTenantDefaults()
    {
        var sut = new EntraNoOpTenantContext();
        sut.IsMultiTenancyEnabled.Should().BeFalse(
            "per-user tenancy doesn't apply to the Entra adapter family — the directory IS the tenant");
        sut.CurrentTenantId.Should().BeNull();
        sut.CurrentTenantDisplayName.Should().BeNull();
    }

    [Fact]
    public void NoOpExternalLoginFlow_Ctor_ExposesProvidedCapabilities()
    {
        var caps = new UserBackendCapabilities { SupportsExternalProviders = false };
        var sut = new EntraNoOpExternalLoginFlow(caps);
        sut.Capabilities.Should().BeSameAs(caps,
            "the LoginModel reads Capabilities off the flow to decide whether to render the providers section");
    }

    [Fact]
    public void NoOpExternalLoginFlow_NullCapabilities_Throws()
    {
        var act = () => new EntraNoOpExternalLoginFlow(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("capabilities");
    }

    [Fact]
    public async Task NoOpExternalLoginFlow_GetProvidersAsync_ReturnsEmpty()
    {
        var sut = new EntraNoOpExternalLoginFlow(new UserBackendCapabilities());
        (await sut.GetProvidersAsync()).Should().BeEmpty(
            "no third-party provider buttons to render — Entra IS the provider");
    }

    [Fact]
    public async Task NoOpExternalLoginFlow_CompleteSignInAsync_ReturnsNoExternalSession()
    {
        var sut = new EntraNoOpExternalLoginFlow(new UserBackendCapabilities());
        var result = await sut.CompleteSignInAsync(ExternalLoginFirstTimeStrategy.AutoCreate);
        result.Outcome.Should().Be(ExternalSignInOutcome.NoExternalSession);
    }

    [Fact]
    public async Task NoOpExternalLoginFlow_ConfirmAndCreateAsync_ReturnsFailedWithExplanation()
    {
        var sut = new EntraNoOpExternalLoginFlow(new UserBackendCapabilities());
        var result = await sut.ConfirmAndCreateAsync("a@b.com", null, null);
        result.Outcome.Should().Be(ExternalSignInOutcome.Failed);
        result.Errors.Should().NotBeEmpty(
            "the stub explains why this path is unreachable from the UI when capabilities are off");
    }

    [Fact]
    public async Task NoOpExternalLoginFlow_GetPendingInfoAsync_ReturnsNull()
    {
        var sut = new EntraNoOpExternalLoginFlow(new UserBackendCapabilities());
        (await sut.GetPendingInfoAsync()).Should().BeNull();
    }
}
