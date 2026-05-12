using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.Identity.MultiTenancy;

/// <summary>
/// Wiring helpers for VisuAuth multi-tenancy. Single-tenant deployments do
/// nothing here — without these calls the default <see cref="NoOpTenantContext"/>
/// is registered by <c>AddVisuAuthIdentityAdapter</c>.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Switches the host into multi-tenant mode: registers
    /// <see cref="HttpContextTenantContext"/>, the save-changes interceptor,
    /// and the <see cref="TenantOptions"/> binding.
    /// </summary>
    public static IServiceCollection EnableVisuAuthTenancy(
        this IServiceCollection services,
        Action<TenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.Configure(configure ?? (_ => { }));

        // Replace any prior ITenantContext registration — NoOp gets installed
        // by AddVisuAuthIdentityAdapter as the default, EnableVisuAuthTenancy
        // upgrades it to the HTTP-aware one.
        services.RemoveAll<ITenantContext>();
        services.AddScoped<ITenantContext, HttpContextTenantContext>();
        services.AddScoped<TenantSaveChangesInterceptor>();

        return services;
    }

    /// <summary>
    /// Mounts the tenant resolver middleware. Call this before
    /// <c>MapVisuAuth</c> so endpoints see the resolved tenant id.
    /// </summary>
    public static IApplicationBuilder UseVisuAuthTenancy(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TenantResolverMiddleware>();
    }
}

/// <summary>
/// EF Core helpers for plugging the tenant interceptor into a consumer
/// DbContext without forcing them to manually pull it from DI.
/// </summary>
public static class TenancyDbContextOptionsExtensions
{
    /// <summary>
    /// Adds <see cref="TenantSaveChangesInterceptor"/> to the DbContext options.
    /// Resolve the service provider from the lambda overload of
    /// <c>AddDbContext</c> so the interceptor sees the per-request tenant.
    /// </summary>
    public static DbContextOptionsBuilder AddVisuAuthTenancy(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var interceptor = serviceProvider.GetService<TenantSaveChangesInterceptor>();
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }
        // When EnableVisuAuthTenancy was not called the interceptor is absent —
        // the consumer is in single-tenant mode and this call becomes a no-op.

        return builder;
    }
}
