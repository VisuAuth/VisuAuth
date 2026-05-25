using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Identity.Auditing;

namespace VisuAuth.Identity.DependencyInjection;

/// <summary>
/// Registration helpers for the opt-in audit log plugin. Without these
/// calls the default <see cref="NoOpAuditWriter"/> handles every
/// <c>IAuditWriter</c> call, so consumer code doesn't have to check
/// whether auditing is enabled before emitting events.
/// </summary>
public static class VisuAuthAuditLogExtensions
{
    /// <summary>
    /// Registers <see cref="NoOpAuditWriter"/> as the default
    /// <see cref="IAuditWriter"/>. Wired automatically inside
    /// <c>AddVisuAuth().UseAspNetIdentity()</c> so handlers can safely
    /// inject <see cref="IAuditWriter"/> without an opt-in step.
    /// </summary>
    internal static IServiceCollection AddVisuAuthAuditWriterDefault(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IAuditWriter, NoOpAuditWriter>();
        return services;
    }

    /// <summary>
    /// Turns the audit log plugin on: replaces the default no-op writer
    /// with <see cref="EfCoreAuditStore"/> (which also implements
    /// <see cref="IAuditReader"/> for the admin page), starts the
    /// background retention service, and registers
    /// <see cref="IHttpContextAccessor"/> if the consumer hasn't already
    /// (the writer needs it to capture actor IP / user agent).
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configure">Optional callback to tweak
    /// <see cref="AuditLogOptions"/> — e.g. <c>opts.RetentionDays = 365</c>.</param>
    public static IServiceCollection AddVisuAuthAuditLog(
        this IServiceCollection services,
        Action<AuditLogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            // Make sure IOptions<AuditLogOptions> resolves even when the
            // consumer didn't pass a configurator — defaults kick in.
            services.AddOptions<AuditLogOptions>();
        }

        services.AddHttpContextAccessor();

        // Replace the default no-op writer with the EF-backed one.
        // EfCoreAuditStore implements BOTH writer and reader — register
        // the same scoped instance under both contracts so a single DB
        // context scope serves both reads (admin page) and writes
        // (handlers) within the same request.
        services.RemoveAll<IAuditWriter>();
        services.AddScoped<EfCoreAuditStore>();
        services.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<EfCoreAuditStore>());
        services.AddScoped<IAuditReader>(sp => sp.GetRequiredService<EfCoreAuditStore>());

        services.AddHostedService<AuditRetentionHostedService>();
        return services;
    }
}
