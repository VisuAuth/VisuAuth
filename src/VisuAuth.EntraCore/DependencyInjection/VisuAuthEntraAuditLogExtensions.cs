using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.EntraCore.Auditing;

namespace VisuAuth.EntraCore.DependencyInjection;

/// <summary>
/// Opt-in registration of the Entra sign-in audit reader. Mirrors the
/// Identity adapter's <c>AddVisuAuthAuditLog()</c>: call it (after
/// <c>AddVisuAuthEntra</c> / <c>AddVisuAuthEntraExternal</c>) to surface
/// the tenant's Entra sign-in events on the admin audit-log page and feed
/// the dashboard "logins per day" chart.
/// </summary>
public static class VisuAuthEntraAuditLogExtensions
{
    /// <summary>
    /// Registers <see cref="EntraSignInAuditReader"/> as the
    /// <see cref="IAuditReader"/>. Reads <c>/auditLogs/signIns</c> through
    /// the <c>GraphServiceClient</c> the Entra adapter already wires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Permissions.</b> The registered app needs the Microsoft Graph
    /// application permission <c>AuditLog.Read.All</c> (admin-consented),
    /// and the tenant needs an <b>Entra ID P1</b> licence — sign-in logs
    /// are a premium feature. Without either, Graph returns 403 and the
    /// reader degrades to an empty view (the page renders its empty state
    /// rather than erroring).
    /// </para>
    /// <para>
    /// Opt-in (not folded into <c>AddVisuAuthEntra</c>) precisely because
    /// of that licence/permission requirement: a consumer who hasn't
    /// granted it should see the "audit not enabled" hint, not an
    /// always-empty page that looks like a bug.
    /// </para>
    /// <para>
    /// <c>TryAdd</c> so a consumer who ALSO wired the EF-backed
    /// <c>AddVisuAuthAuditLog()</c> (e.g. a hybrid deployment) keeps their
    /// explicit reader — last-registration-wins isn't the contract here;
    /// first-wins via TryAdd is.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVisuAuthEntraSignInAuditLog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IAuditReader, EntraSignInAuditReader>();
        return services;
    }
}
