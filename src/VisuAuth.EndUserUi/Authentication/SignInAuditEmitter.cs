using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EndUserUi.Authentication;

/// <summary>
/// Builds and writes the <see cref="AuditEvent"/> for a sign-in attempt.
/// Wraps <see cref="SignInAuditMapper"/> with the boilerplate every caller
/// would otherwise duplicate (TargetType = User, TargetLabel = email,
/// channel payload, extra payload merge).
/// </summary>
/// <remarks>
/// Registered as <c>Scoped</c> in DI so it inherits the request-scoped
/// <see cref="IAuditWriter"/> when the audit plugin is on (and the no-op
/// writer otherwise — the caller never has to know which).
/// </remarks>
public sealed class SignInAuditEmitter(IAuditWriter audit)
{
    /// <summary>Payload key for the entry-point channel ("web", "api", …).</summary>
    public const string ChannelPayloadKey = "channel";

    private readonly IAuditWriter _audit = audit ?? throw new ArgumentNullException(nameof(audit));

    /// <summary>
    /// Writes one audit entry for the given sign-in result, or no entry
    /// when the mapper returns null (e.g. RedirectToExternalProvider).
    /// </summary>
    /// <param name="result">Outcome from <c>IAuthenticationFlow.SignInWithPasswordAsync</c>.</param>
    /// <param name="attemptedEmail">Email the user typed — used as TargetLabel even on failure so the admin page can list the offender.</param>
    /// <param name="channel">Which surface produced the attempt — added to payload.</param>
    /// <param name="extraPayload">Optional channel-specific keys (e.g. <c>rememberMe</c> from the Web channel). Merged on top of the channel key.</param>
    /// <param name="cancellationToken">Standard cancellation.</param>
    public Task EmitAsync(
        SignInResult result,
        string attemptedEmail,
        SignInChannel channel,
        IReadOnlyDictionary<string, string?>? extraPayload = null,
        CancellationToken cancellationToken = default)
    {
        var shape = SignInAuditMapper.FromOutcome(result);
        if (shape is null)
        {
            return Task.CompletedTask;
        }

        return _audit.WriteAsync(new AuditEvent
        {
            Action = shape.Action,
            TargetType = AuditTargetTypes.User,
            TargetId = result.UserId,
            TargetLabel = attemptedEmail,
            Outcome = shape.Outcome,
            FailureReason = shape.FailureReason,
            Payload = BuildPayload(channel, extraPayload),
        }, cancellationToken);
    }

    private static Dictionary<string, string?> BuildPayload(
        SignInChannel channel,
        IReadOnlyDictionary<string, string?>? extra)
    {
        var payload = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // enum names are ASCII so ToLowerInvariant is culture-safe.
            [ChannelPayloadKey] = channel.ToString().ToLowerInvariant(),
        };
        if (extra is null)
        {
            return payload;
        }
        foreach (var pair in extra)
        {
            payload[pair.Key] = pair.Value;
        }
        return payload;
    }
}
