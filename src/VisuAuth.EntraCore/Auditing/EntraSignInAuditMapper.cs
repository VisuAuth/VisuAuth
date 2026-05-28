using System.Globalization;
using System.Text.Json;
using Microsoft.Graph.Models;
using VisuAuth.Abstractions.Auditing;

namespace VisuAuth.EntraCore.Auditing;

/// <summary>
/// Pure projections + OData <c>$filter</c> construction for the Entra
/// sign-in audit reader. Static so the mapping and filter logic can be
/// unit-tested without a <see cref="Microsoft.Graph.GraphServiceClient"/>.
/// </summary>
/// <remarks>
/// Sign-in events map onto VisuAuth's canonical
/// <see cref="AuditActions.LoginSucceeded"/> / <see cref="AuditActions.LoginFailed"/>
/// action codes (rather than Entra-specific strings) so the admin
/// filter dropdown and the dashboard "logins per day" chart — which calls
/// <see cref="IAuditReader.CountByDayAsync"/> with the canonical login
/// code — line up across backends.
/// </remarks>
internal static class EntraSignInAuditMapper
{
    /// <summary>Graph caps a single signIns page at 1000.</summary>
    public const int MaxGraphPageSize = 1000;

    /// <summary>
    /// Projects a Graph <see cref="SignIn"/> onto the read-shape the admin
    /// page renders. A failed sign-in (non-zero status error code) maps to
    /// <see cref="AuditOutcome.Failure"/> + <see cref="AuditActions.LoginFailed"/>.
    /// </summary>
    public static AuditEntryView ToEntryView(SignIn signIn)
    {
        ArgumentNullException.ThrowIfNull(signIn);

        var succeeded = (signIn.Status?.ErrorCode ?? 0) == 0;
        return new AuditEntryView
        {
            // signIn.Id is a GUID string in practice; fall back to a fresh
            // GUID for the (non-persisted) row key if it ever isn't.
            Id = Guid.TryParse(signIn.Id, out var id) ? id : Guid.NewGuid(),
            Timestamp = signIn.CreatedDateTime ?? DateTimeOffset.MinValue,
            Action = succeeded ? AuditActions.LoginSucceeded : AuditActions.LoginFailed,
            TargetType = AuditTargetTypes.User,
            TargetId = signIn.UserId,
            TargetLabel = signIn.UserPrincipalName,
            Outcome = succeeded ? AuditOutcome.Success : AuditOutcome.Failure,
            FailureReason = succeeded ? null : signIn.Status?.FailureReason,
            ActorUserId = signIn.UserId,
            ActorEmail = signIn.UserPrincipalName,
            ActorIpAddress = signIn.IpAddress,
            // The v1.0 signIn resource has no raw user-agent field, so this
            // stays null rather than inventing one from device details.
            ActorUserAgent = null,
            // The directory IS the tenant for the Entra adapters — per-user
            // tenancy doesn't apply, so no VisuAuth tenant id.
            TenantId = null,
            PayloadJson = BuildPayload(signIn),
        };
    }

    /// <summary>
    /// Builds the <c>$filter</c> for the list query from an
    /// <see cref="AuditFilter"/>. Returns null when no clause applies
    /// (Graph rejects an empty filter string).
    /// </summary>
    public static string? BuildListFilter(AuditFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var clauses = new List<string>(4);

        AddDateRange(clauses, filter.From, filter.To);

        if (!string.IsNullOrWhiteSpace(filter.ActorSearch))
        {
            clauses.Add($"startswith(userPrincipalName,'{Escape(filter.ActorSearch.Trim())}')");
        }

        // Outcome can come from the explicit Outcome field OR be implied by
        // the canonical login action code. Either narrows to the matching
        // status/errorCode predicate.
        var outcomeClause = ResolveOutcomeClause(filter.Outcome, filter.Action);
        if (outcomeClause is not null)
        {
            clauses.Add(outcomeClause);
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    /// <summary>
    /// Builds the <c>$filter</c> for a day-rollup of a single action code.
    /// Returns null when <paramref name="action"/> isn't one this reader
    /// can count (only the login codes map to sign-ins) — the caller then
    /// returns an empty series.
    /// </summary>
    public static string? BuildCountFilter(string action, DateTimeOffset fromInclusive, DateTimeOffset toInclusive)
    {
        var outcomeClause = action switch
        {
            AuditActions.LoginSucceeded => "status/errorCode eq 0",
            AuditActions.LoginFailed => "status/errorCode ne 0",
            _ => null,
        };
        if (outcomeClause is null)
        {
            return null;
        }

        var clauses = new List<string>(3) { outcomeClause };
        AddDateRange(clauses, fromInclusive, toInclusive);
        return string.Join(" and ", clauses);
    }

    private static void AddDateRange(List<string> clauses, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is { } f)
        {
            clauses.Add($"createdDateTime ge {FormatInstant(f)}");
        }
        if (to is { } t)
        {
            clauses.Add($"createdDateTime le {FormatInstant(t)}");
        }
    }

    private static string? ResolveOutcomeClause(AuditOutcome? outcome, string? action)
    {
        if (outcome is AuditOutcome.Success || action == AuditActions.LoginSucceeded)
        {
            return "status/errorCode eq 0";
        }
        if (outcome is AuditOutcome.Failure || action == AuditActions.LoginFailed)
        {
            return "status/errorCode ne 0";
        }
        return null;
    }

    private static string? BuildPayload(SignIn signIn)
    {
        // Small contextual payload — app + client app the sign-in used.
        // Null when neither is present so the page doesn't render an empty
        // "{}" badge.
        var app = signIn.AppDisplayName;
        var clientApp = signIn.ClientAppUsed;
        if (string.IsNullOrEmpty(app) && string.IsNullOrEmpty(clientApp))
        {
            return null;
        }
        var payload = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(app))
        {
            payload["app"] = app;
        }
        if (!string.IsNullOrEmpty(clientApp))
        {
            payload["clientApp"] = clientApp;
        }
        return JsonSerializer.Serialize(payload);
    }

    private static string FormatInstant(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Escape(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
