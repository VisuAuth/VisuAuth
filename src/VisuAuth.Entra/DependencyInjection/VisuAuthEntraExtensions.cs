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

    private static IServiceCollection RegisterCore(IServiceCollection services)
    {
        // Singleton GraphServiceClient — Microsoft.Graph v5 supports
        // concurrent use from multiple threads, and the wrapped
        // ClientSecretCredential caches tokens for their lifetime, so the
        // cost of building the client is paid once per process. Factory
        // lives in VisuAuth.EntraCore so VisuAuth.EntraExternal can reuse
        // it (both adapters auth the same way).
        services.TryAddSingleton<GraphServiceClient>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EntraOptions>>().Value;
            return EntraGraphClientFactory.Create(opts.TenantId, opts.ClientId, opts.ClientSecret);
        });

        services.TryAddScoped<IUserStore, EntraUserStore>();
        services.TryAddScoped<IRoleStore, EntraRoleStore>();
        services.TryAddScoped<IAuthenticationFlow, EntraAuthenticationFlow>();
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
