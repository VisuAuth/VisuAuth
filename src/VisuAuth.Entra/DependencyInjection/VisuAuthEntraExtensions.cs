using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Graph;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Abstractions.Users;
using VisuAuth.Entra.Configuration;
using VisuAuth.EntraCore.Infrastructure;
using VisuAuth.EntraCore.Stubs;

namespace VisuAuth.Entra.DependencyInjection;

/// <summary>
/// Composition root for the Microsoft Entra ID adapter. The consumer's
/// <c>Program.cs</c> typically wires VisuAuth like:
/// <code>
/// builder.Services
///     .AddVisuAuth()
///     .AddAdminUi();
/// builder.Services
///     .AddVisuAuthEntra(o =>
///     {
///         o.TenantId = builder.Configuration["VisuAuth:Entra:TenantId"]!;
///         o.ClientId = builder.Configuration["VisuAuth:Entra:ClientId"]!;
///         o.ClientSecret = builder.Configuration["VisuAuth:Entra:ClientSecret"]!;
///     });
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a <see cref="IServiceCollection"/> extension (not a fluent
/// chain on <c>IVisuAuthBuilder</c>) so the Entra adapter package keeps a
/// one-way dependency on <c>VisuAuth.Abstractions</c> and never references
/// the meta-package. The Identity adapter does the same.
/// </para>
/// <para>
/// What gets registered:
/// </para>
/// <list type="bullet">
///   <item><see cref="EntraOptions"/> bound + validated.</item>
///   <item><see cref="GraphServiceClient"/> as a singleton — Graph SDK
///     v5 is thread-safe and re-uses one <see cref="HttpClient"/>
///     internally, so a singleton matches the recommended pattern and
///     avoids per-request socket churn.</item>
///   <item><see cref="EntraUserStore"/> as <see cref="IUserStore"/>.</item>
///   <item><see cref="EntraRoleStore"/> as <see cref="IRoleStore"/>.</item>
///   <item><see cref="EntraAuthenticationFlow"/> as
///     <see cref="IAuthenticationFlow"/> — the capability-flag shim that
///     tells the end-user UI "Microsoft owns the login, don't render the
///     password form".</item>
/// </list>
/// All four registrations use <c>TryAdd</c> so a consumer can override any
/// of them (e.g. swap in a fake during integration tests) by registering
/// their replacement BEFORE calling <c>AddVisuAuthEntra</c>.
/// </remarks>
public static class VisuAuthEntraExtensions
{
    /// <summary>Default configuration section the binder reads from.</summary>
    public const string DefaultConfigurationSection = "VisuAuth:Entra";

    /// <summary>
    /// Wires the Entra adapter with an inline configuration lambda — the
    /// shortest path when settings come from code, environment variables,
    /// or a custom resolver rather than <c>appsettings.json</c>.
    /// </summary>
    public static IServiceCollection AddVisuAuthEntra(
        this IServiceCollection services,
        Action<EntraOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<EntraOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    /// <summary>
    /// Wires the Entra adapter from a configuration section. Defaults to
    /// <see cref="DefaultConfigurationSection"/> (<c>"VisuAuth:Entra"</c>).
    /// </summary>
    public static IServiceCollection AddVisuAuthEntra(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services
            .AddOptions<EntraOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    /// <summary>
    /// Opt-in: lets admins edit the Entra adapter's settings (TenantId,
    /// ClientId, ClientSecret, …) from <c>/visuauth/admin/entra-config</c> and
    /// persist them to the database, overlaid on top of the values bound from
    /// code / appsettings / user-secrets. A save takes effect on the next Graph
    /// call without a restart. Call AFTER <c>AddVisuAuthEntra</c>, and ensure
    /// the DB store is registered via <c>AddVisuAuthAdapterConfigStore()</c>
    /// (from <c>VisuAuth.Identity</c>) against the consumer's metadata DbContext.
    /// </summary>
    public static IServiceCollection AddVisuAuthEntraDbConfig(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<EntraConfigStaticSnapshot>();
        services.TryAddSingleton<EntraConfigChangeSignal>();

        // The overlay must run AFTER the consumer's Bind/Configure so a DB
        // value wins; registration order in the IConfigureOptions chain is
        // preserved, and AddVisuAuthEntra (which binds) runs first.
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<EntraOptions>>(sp =>
            new EntraDbConfigOverlay(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<EntraConfigStaticSnapshot>()));

        // Wiring the change-token source is what makes IOptionsMonitor recompute
        // (and the Graph client rebuild) after a save.
        services.AddSingleton<Microsoft.Extensions.Options.IOptionsChangeTokenSource<EntraOptions>>(sp =>
            new EntraConfigChangeTokenSource(sp.GetRequiredService<EntraConfigChangeSignal>()));

        // The admin page resolves IAdapterConfigSchema to render the editor.
        services.AddSingleton<VisuAuth.Abstractions.Configuration.IAdapterConfigSchema, EntraAdapterConfigSchema>();

        // The page calls this after a save to trigger the options recompute.
        services.AddSingleton<VisuAuth.Abstractions.Configuration.IAdapterConfigChangeNotifier, EntraConfigChangeNotifier>();

        return services;
    }

    private static IServiceCollection RegisterCore(IServiceCollection services)
    {
        // EntraGraphClientProvider (singleton) owns the GraphServiceClient and
        // rebuilds it lazily when the effective EntraOptions change — reading
        // IOptionsMonitor<EntraOptions>, which re-materializes after an admin
        // save (see AddVisuAuthEntraDbConfig). The stores resolve it through
        // IEntraGraphClient and call GetClient() per operation so they observe
        // the latest client without holding a DI-tracked instance that the
        // container would dispose at scope end. GraphServiceClient itself is a
        // singleton sourced from the provider — kept for consumers that inject
        // it directly (e.g. the opt-in EntraCore audit reader); the container
        // disposes that one instance once at shutdown.
        services.TryAddSingleton<EntraGraphClientProvider>();
        services.TryAddSingleton<IEntraGraphClient>(sp =>
            sp.GetRequiredService<EntraGraphClientProvider>());
        services.TryAddSingleton<GraphServiceClient>(sp =>
            sp.GetRequiredService<IEntraGraphClient>().GetClient());

        services.TryAddScoped<IUserStore, EntraUserStore>();
        services.TryAddScoped<IRoleStore, EntraRoleStore>();
        services.TryAddScoped<IAuthenticationFlow, EntraAuthenticationFlow>();
        // Surfaces the tenant's verified domains (Graph /domains, cached)
        // so the admin Create-User form can render a domain dropdown for a
        // multi-domain tenant instead of the single configured suffix.
        // Singleton — the list is process-stable and the form calls it on
        // every render. Optional from the page's perspective (it resolves
        // IEmailDomainSource with a null default), so a tenant with one
        // domain keeps the existing locked-suffix UX.
        services.TryAddSingleton<IEmailDomainSource, EntraEmailDomainSource>();
        // The shared EntraNoOpExternalLoginFlow takes a UserBackendCapabilities
        // in its ctor so each adapter (Workforce / External) can hand its
        // own caps bag — the LoginModel reads Capabilities.SupportsExternalProviders
        // off the flow to decide whether to render the providers section.
        services.TryAddScoped<IExternalLoginFlow>(_ => new EntraNoOpExternalLoginFlow(EntraCapabilities.Value));

        // VisuAuth.EndUserUi's SignInAuditEmitter depends on IAuditWriter
        // unconditionally. The Identity adapter registers a NoOpAuditWriter
        // as fallback; an Entra-only deployment has no Identity wire-up, so
        // without this line every sign-in attempt would crash with
        // "Unable to resolve IAuditWriter". TryAdd preserves the real
        // EfCoreAuditStore when the consumer ALSO wires AddVisuAuthAuditLog.
        services.TryAddSingleton<IAuditWriter, EntraNoOpAuditWriter>();

        // Same story for IJwtIssuer — the minimal-API /api/auth/login
        // endpoint mapped by MapVisuAuthEndUserUi lists it as a required
        // parameter, so startup fails without one. The Entra mobile flow
        // doesn't need our HS256 issuer (Microsoft issues its own tokens),
        // so a stub that always returns null is the right shape; the API
        // surfaces a clean 401 via the existing IssueOrUnauthorized branch.
        services.TryAddSingleton<IJwtIssuer, EntraNoOpJwtIssuer>();

        // And ITenantContext — VisuAuth.Identity registers
        // HttpContextTenantContext only when EnableMultiTenant fires; an
        // Entra-only deployment skips that step entirely, but the minimal
        // API's RegisterAsync handler still expects ITenantContext as a
        // constructor param. The no-op reports IsMultiTenancyEnabled =
        // false, which is the right answer for Entra (the directory IS
        // the tenant — per-user tenancy doesn't apply).
        services.TryAddSingleton<ITenantContext, EntraNoOpTenantContext>();

        return services;
    }
}
