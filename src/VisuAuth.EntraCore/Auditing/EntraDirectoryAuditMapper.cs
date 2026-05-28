using System.Globalization;
using System.Text.Json;
using Microsoft.Graph.Models;
using VisuAuth.Abstractions.Auditing;

namespace VisuAuth.EntraCore.Auditing;

/// <summary>
/// Pure projection + OData <c>$filter</c> construction for the Entra
/// <c>/auditLogs/directoryAudits</c> source — the directory-change trail
/// (user created / updated / deleted, role assignments, etc., including
/// the operations VisuAuth itself performs, as Graph logs them).
/// Complements <see cref="EntraSignInAuditMapper"/>; the two feed the
/// composite <see cref="EntraAuditReader"/>.
/// </summary>
/// <remarks>
/// Unlike sign-ins (which map onto the canonical login codes), a directory
/// audit's <see cref="AuditEntryView.Action"/> is Entra's own
/// <c>activityDisplayName</c> (e.g. "Add user", "Add member to role") —
/// free text VisuAuth doesn't try to translate into its
/// <see cref="AuditActions"/> codes, because the activity vocabulary is
/// Entra's and is localised. The admin filter dropdown therefore lists
/// these raw activity names alongside the two login codes.
/// </remarks>
internal static class EntraDirectoryAuditMapper
{
    /// <summary>Projects a Graph <see cref="DirectoryAudit"/> onto the audit read-shape.</summary>
    public static AuditEntryView ToEntryView(DirectoryAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        // null Result is treated as success — most directory activities
        // carry an explicit result, and a benign default avoids mislabeling
        // a routine change as a failure.
        var succeeded = audit.Result is null
            or OperationResult.Success
            or OperationResult.UnknownFutureValue;

        var target = audit.TargetResources is { Count: > 0 } ? audit.TargetResources[0] : null;
        var initiator = audit.InitiatedBy;

        return new AuditEntryView
        {
            Id = Guid.TryParse(audit.Id, out var id) ? id : Guid.NewGuid(),
            Timestamp = audit.ActivityDateTime ?? DateTimeOffset.MinValue,
            Action = string.IsNullOrEmpty(audit.ActivityDisplayName)
                ? "DirectoryActivity"
                : audit.ActivityDisplayName,
            TargetType = string.IsNullOrEmpty(target?.Type) ? "DirectoryObject" : target.Type,
            TargetId = target?.Id,
            TargetLabel = target?.DisplayName ?? target?.UserPrincipalName,
            Outcome = succeeded ? AuditOutcome.Success : AuditOutcome.Failure,
            FailureReason = succeeded ? null : audit.ResultReason,
            ActorUserId = initiator?.User?.Id,
            ActorEmail = initiator?.User?.UserPrincipalName ?? initiator?.App?.DisplayName,
            ActorIpAddress = initiator?.User?.IpAddress,
            ActorUserAgent = null,
            TenantId = null,
            PayloadJson = BuildPayload(audit),
        };
    }

    /// <summary>
    /// Builds the <c>$filter</c> for the directory-audit query. Pushes the
    /// date range, the result/outcome, and an exact activity match (when
    /// the caller filters by a specific action) to Graph. Actor search is
    /// applied client-side by the reader — <c>directoryAudits</c> doesn't
    /// reliably filter on the nested <c>initiatedBy/user</c> path. Returns
    /// null when no clause applies.
    /// </summary>
    public static string? BuildListFilter(AuditFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var clauses = new List<string>(4);

        if (filter.From is { } from)
        {
            clauses.Add($"activityDateTime ge {FormatInstant(from)}");
        }
        if (filter.To is { } to)
        {
            clauses.Add($"activityDateTime le {FormatInstant(to)}");
        }
        if (filter.Outcome is AuditOutcome.Success)
        {
            clauses.Add("result eq 'success'");
        }
        else if (filter.Outcome is AuditOutcome.Failure)
        {
            clauses.Add("result eq 'failure'");
        }
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            clauses.Add($"activityDisplayName eq '{Escape(filter.Action.Trim())}'");
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    private static string? BuildPayload(DirectoryAudit audit)
    {
        if (string.IsNullOrEmpty(audit.Category))
        {
            return null;
        }
        return JsonSerializer.Serialize(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["category"] = audit.Category,
        });
    }

    private static string FormatInstant(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Escape(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
