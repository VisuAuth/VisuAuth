using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VisuAuth.AdminUi.DependencyInjection;
using VisuAuth.EndUserUi.DependencyInjection;
using VisuAuth.Identity.DependencyInjection;
using VisuAuth.Identity.MultiTenancy;
using TenantOptions = VisuAuth.Abstractions.Tenancy.TenantOptions;

namespace VisuAuth;

/// <summary>
/// Fluent chain methods exposed on <see cref="IVisuAuthBuilder"/>. Each one
/// delegates to the granular service-collection extensions in the underlying
/// packages so the fluent surface stays a thin shim and the lower-level API
/// remains usable on its own.
/// </summary>
public static class VisuAuthFluentExtensions
{
    /// <summary>
    /// Wires VisuAuth's user and role stores against ASP.NET Core Identity.
    /// Must be paired with a <c>builder.Services.AddIdentity&lt;TUser, IdentityRole&gt;()</c>
    /// (or EF Core variant) so <c>UserManager</c> / <c>RoleManager</c> are
    /// resolvable at runtime.
    /// </summary>
    /// <typeparam name="TUser">The Identity user type used by the consumer (e.g. <c>ApplicationUser</c>).</typeparam>
    public static IVisuAuthBuilder UseAspNetIdentity<TUser>(this IVisuAuthBuilder builder)
        where TUser : IdentityUser
        => builder.UseAspNetIdentity<TUser, IdentityRole>();

    /// <summary>
    /// Wires VisuAuth's user and role stores against ASP.NET Core Identity with
    /// an explicit role type. Use this overload when the consumer extends
    /// <see cref="IdentityRole"/>.
    /// </summary>
    /// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
    /// <typeparam name="TRole">The Identity role type used by the consumer.</typeparam>
    public static IVisuAuthBuilder UseAspNetIdentity<TUser, TRole>(this IVisuAuthBuilder builder)
        where TUser : IdentityUser
        where TRole : IdentityRole, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddVisuAuthIdentityAdapter<TUser, TRole>();
        return builder;
    }

    /// <summary>
    /// Switches the host into multi-tenant mode. Registers the tenant resolver
    /// middleware deps and the <see cref="TenantSaveChangesInterceptor"/> as a
    /// scoped service. The consumer is still responsible for calling
    /// <c>options.AddVisuAuthTenancy(sp)</c> inside their
    /// <c>AddDbContext</c> lambda so the interceptor reaches the DbContext.
    /// This overload does NOT register the tenant catalogue store — use the
    /// generic overload below to also light up <c>/visuauth/admin/tenants</c>.
    /// </summary>
    public static IVisuAuthBuilder EnableMultiTenant(
        this IVisuAuthBuilder builder,
        Action<TenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.EnableVisuAuthTenancy(configure);
        return builder;
    }

    /// <summary>
    /// Multi-tenant mode plus the tenant catalogue store, so the admin
    /// dashboard's <c>/visuauth/admin/tenants</c> page can list / create /
    /// rename / delete tenants against the consumer's own DbContext.
    /// </summary>
    /// <typeparam name="TDbContext">The consumer DbContext type (typically extends
    /// <see cref="MultiTenantIdentityDbContext{TUser}"/>). Required because the
    /// catalogue store reads / writes via <see cref="IVisuAuthMetadataDbContext"/>.</typeparam>
    /// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
    public static IVisuAuthBuilder EnableMultiTenant<TDbContext, TUser>(
        this IVisuAuthBuilder builder,
        Action<TenantOptions>? configure = null)
        where TDbContext : DbContext, IVisuAuthMetadataDbContext
        where TUser : IdentityUser, IMultiTenantEntity
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.EnableVisuAuthTenancy<TDbContext, TUser>(configure);
        return builder;
    }

    /// <summary>
    /// Adds the admin dashboard surface (Razor Pages, htmx, default theme,
    /// localization resources). Idempotent — safe to call multiple times.
    /// </summary>
    public static IVisuAuthBuilder AddAdminUi(this IVisuAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddVisuAuthAdminUi();
        return builder;
    }

    /// <summary>
    /// Adds the end-user authentication pages (login, register, password
    /// reset, confirm email) plus the mobile JWT / WebView API channel.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    public static IVisuAuthBuilder AddEndUserUi(this IVisuAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddVisuAuthEndUserUi();
        return builder;
    }
}
