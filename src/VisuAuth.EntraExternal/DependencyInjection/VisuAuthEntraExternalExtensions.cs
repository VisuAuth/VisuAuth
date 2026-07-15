using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Graph;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Abstractions.Users;
using VisuAuth.EntraCore.Infrastructure;
using VisuAuth.EntraCore.Stubs;
using VisuAuth.EntraExternal.Configuration;

namespace VisuAuth.EntraExternal.DependencyInjection;

/// <summary>
/// Composition root for the Microsoft Entra External ID adapter. The
/// consumer's <c>Program.cs</c> typically wires VisuAuth like:
/// <code>
/// builder.Services
///     .AddVisuAuth()
///     .AddAdminUi()
///     .AddEndUserUi();
/// builder.Services
///     .AddVisuAuthEntraExternal(o =>
///     {
///         o.TenantId = builder.Configuration["VisuAuth:EntraExternal:TenantId"]!;
///         o.ClientId = builder.Configuration["VisuAuth:EntraExternal:ClientId"]!;
///         o.ClientSecret = builder.Configuration["VisuAuth:EntraExternal:ClientSecret"]!;
///         o.TenantDomain = builder.Configuration["VisuAuth:EntraExternal:TenantDomain"]!;
///     });
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the Workforce <c>AddVisuAuthEntra</c> shape: an
/// <see cref="IServiceCollection"/> extension (not fluent on
/// <c>IVisuAuthBuilder</c>) so the package keeps a one-way dependency on
/// <c>VisuAuth.Abstractions</c> and never references the meta-package.
/// </para>
/// <para>
/// What gets registered (<see cref="ServiceCollectionDescriptorExtensions.TryAdd"/>
/// throughout, so a consumer-registered test double wins when registered
/// BEFORE this call):
/// </para>
/// <list type="bullet">
///   <item><see cref="EntraExternalOptions"/> bound + validated.</item>
///   <item><see cref="GraphServiceClient"/> as a singleton via
///     <see cref="EntraGraphClientFactory.Create"/> — same factory the
///     Workforce adapter uses, hosted in VisuAuth.EntraCore.</item>
///   <item><see cref="EntraExternalUserStore"/> as
///     <see cref="IUserStore"/>.</item>
///   <item><see cref="EntraExternalRoleStore"/> as
///     <see cref="IRoleStore"/>.</item>
///   <item><see cref="EntraExternalAuthenticationFlow"/> as
///     <see cref="IAuthenticationFlow"/> — the capability-flag shim that
///     tells the end-user UI "Microsoft owns the login".</item>
///   <item><see cref="EntraNoOpAuditWriter"/> / <see cref="EntraNoOpJwtIssuer"/>
///     / <see cref="EntraNoOpTenantContext"/> / <see cref="EntraNoOpExternalLoginFlow"/>
///     fallbacks so the End-user UI pipeline resolves cleanly in
///     External-only deployments (no Identity adapter wired
///     alongside).</item>
/// </list>
/// </remarks>
public static class VisuAuthEntraExternalExtensions
{
    /// <summary>Default configuration section the binder reads from.</summary>
    public const string DefaultConfigurationSection = "VisuAuth:EntraExternal";

    /// <summary>
    /// Wires the Entra External adapter with an inline configuration
    /// lambda — the shortest path when settings come from code,
    /// environment variables, or a custom resolver rather than
    /// <c>appsettings.json</c>.
    /// </summary>
    public static IServiceCollection AddVisuAuthEntraExternal(
        this IServiceCollection services,
        Action<EntraExternalOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<EntraExternalOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    /// <summary>
    /// Wires the Entra External adapter from a configuration section.
    /// Defaults to <see cref="DefaultConfigurationSection"/>
    /// (<c>"VisuAuth:EntraExternal"</c>).
    /// </summary>
    public static IServiceCollection AddVisuAuthEntraExternal(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services
            .AddOptions<EntraExternalOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    private static IServiceCollection RegisterCore(IServiceCollection services)
    {
        // Singleton GraphServiceClient — Microsoft.Graph v5 supports
        // concurrent use across threads, and the wrapped
        // ClientSecretCredential caches tokens for their lifetime, so the
        // cost of building the client is paid once per process. Factory
        // lives in VisuAuth.EntraCore so this adapter and the Workforce
        // adapter share the same auth wiring.
        services.TryAddSingleton<GraphServiceClient>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EntraExternalOptions>>().Value;
            return EntraGraphClientFactory.Create(opts.TenantId, opts.ClientId, opts.ClientSecret);
        });

        services.TryAddScoped<IUserStore, EntraExternalUserStore>();
        services.TryAddScoped<IRoleStore, EntraExternalRoleStore>();
        services.TryAddScoped<IAuthenticationFlow, EntraExternalAuthenticationFlow>();
        // The shared EntraNoOpExternalLoginFlow takes UserBackendCapabilities
        // in its ctor so each adapter (Workforce / External) hands its own
        // caps bag — the LoginModel reads Capabilities.SupportsExternalProviders
        // off the flow to decide whether to render the providers section.
        // External declares SupportsExternalProviders = false at this layer
        // because the federated providers (Google, Facebook, Apple) live
        // on the hosted Microsoft login page, not on VisuAuth's surface.
        services.TryAddScoped<IExternalLoginFlow>(_ => new EntraNoOpExternalLoginFlow(EntraExternalCapabilities.Value));

        // SignInAuditEmitter in VisuAuth.EndUserUi depends on IAuditWriter
        // unconditionally. An Entra-External-only deployment has no
        // Identity wiring, so without this line every sign-in attempt
        // would crash with "Unable to resolve IAuditWriter". TryAdd keeps
        // a real EfCoreAuditStore (when AddVisuAuthAuditLog ALSO fires)
        // intact — the consumer can opt into the EF-backed audit store
        // independently.
        services.TryAddSingleton<IAuditWriter, EntraNoOpAuditWriter>();

        // /api/auth/login (mapped by MapVisuAuthEndUserUi) lists IJwtIssuer
        // as a required parameter, so startup fails without one. The
        // External mobile flow doesn't need our HS256 issuer (Microsoft
        // issues its own tokens); the stub that always returns null is
        // the right shape, and the API surfaces a clean 401 via the
        // existing IssueOrUnauthorized branch.
        services.TryAddSingleton<IJwtIssuer, EntraNoOpJwtIssuer>();

        // IJwtValidator — required by /api/auth/refresh. Same no-op rationale
        // as IJwtIssuer above: Entra issues its own tokens, so the stub
        // returns null and the refresh endpoint answers 401.
        services.TryAddSingleton<IJwtValidator, EntraNoOpJwtValidator>();

        // IRefreshTokenService — required by the auth API. Entra issues its own
        // tokens, so the no-op reports disabled and the endpoints keep their
        // Entra-appropriate behaviour.
        services.TryAddSingleton<IRefreshTokenService, EntraNoOpRefreshTokenService>();

        // ITenantContext — VisuAuth.Identity registers HttpContextTenantContext
        // only when EnableMultiTenant fires; an Entra-only deployment skips
        // that step entirely. The minimal API's RegisterAsync handler still
        // takes ITenantContext as a constructor param, and the no-op
        // reports IsMultiTenancyEnabled = false (the directory IS the
        // tenant — per-user tenancy doesn't apply).
        services.TryAddSingleton<ITenantContext, EntraNoOpTenantContext>();

        return services;
    }
}
