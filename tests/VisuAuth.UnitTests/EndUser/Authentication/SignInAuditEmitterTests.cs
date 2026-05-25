using FluentAssertions;
using Moq;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.EndUserUi.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.EndUser.Authentication;

/// <summary>
/// Verifies the emitter's wiring: it consults the mapper, builds an
/// AuditEvent with channel + extra payload merged, and delegates to
/// IAuditWriter. Outcomes the mapper skips (RedirectToExternalProvider)
/// must NOT touch the writer.
/// </summary>
public sealed class SignInAuditEmitterTests
{
    [Fact]
    public async Task EmitAsync_OnSuccess_WritesEventWithChannelAndExtraPayloadMerged()
    {
        var writer = new Mock<IAuditWriter>();
        AuditEvent? captured = null;
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
              .Callback<AuditEvent, CancellationToken>((evt, _) => captured = evt)
              .Returns(Task.CompletedTask);

        var emitter = new SignInAuditEmitter(writer.Object);

        await emitter.EmitAsync(
            new SignInResult { Outcome = SignInOutcome.Success, UserId = "u-1" },
            "alice@example.com",
            SignInChannel.Web,
            extraPayload: new Dictionary<string, string?> { ["rememberMe"] = "true" });

        writer.Verify(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.LoginSucceeded);
        captured.TargetId.Should().Be("u-1");
        captured.TargetLabel.Should().Be("alice@example.com");
        captured.Payload.Should().NotBeNull();
        captured.Payload![SignInAuditEmitter.ChannelPayloadKey].Should().Be("web");
        captured.Payload["rememberMe"].Should().Be("true");
    }

    [Fact]
    public async Task EmitAsync_OnApiChannel_TagsPayloadWithApi()
    {
        var writer = new Mock<IAuditWriter>();
        AuditEvent? captured = null;
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
              .Callback<AuditEvent, CancellationToken>((evt, _) => captured = evt)
              .Returns(Task.CompletedTask);

        var emitter = new SignInAuditEmitter(writer.Object);
        await emitter.EmitAsync(
            new SignInResult { Outcome = SignInOutcome.LockedOut },
            "bob@example.com",
            SignInChannel.Api);

        captured!.Payload![SignInAuditEmitter.ChannelPayloadKey].Should().Be("api");
        captured.Action.Should().Be(AuditActions.LoginLockedOut);
        captured.Outcome.Should().Be(AuditOutcome.Failure);
    }

    [Fact]
    public async Task EmitAsync_OnRedirectToExternalProvider_DoesNotInvokeTheWriter()
    {
        var writer = new Mock<IAuditWriter>(MockBehavior.Strict);
        var emitter = new SignInAuditEmitter(writer.Object);

        await emitter.EmitAsync(
            new SignInResult { Outcome = SignInOutcome.RedirectToExternalProvider },
            "carol@example.com",
            SignInChannel.Web);

        // Strict mock would throw if the writer was touched — reaching this
        // line means the emitter respected the mapper's "skip" signal.
        writer.VerifyNoOtherCalls();
    }
}
