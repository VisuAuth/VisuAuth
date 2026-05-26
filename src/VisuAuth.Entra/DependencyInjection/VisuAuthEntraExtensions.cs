using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Graph;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Roles;
using VisuAuth.Abstractions.Users;
using VisuAuth.Entra.Configuration;

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

    // App-only flows always request every permission granted to the
    // registered app via admin consent; the .default scope is the standard
    // way to express that. Hoisted to a static readonly array to satisfy
    // CA1861 ("avoid allocating a fresh array each call").
    private static readonly string[] GraphDefaultScopes =
        { "https://graph.microsoft.com/.default" };

    private static IServiceCollection RegisterCore(IServiceCollection services)
    {
        // GraphServiceClient is registered as a singleton — Microsoft.Graph
        // v5 explicitly supports concurrent use from multiple threads. The
        // ClientSecretCredential it wraps caches tokens for the rest of
        // their lifetime, so the cost of building the client is paid once
        // per process.
        services.TryAddSingleton<GraphServiceClient>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EntraOptions>>().Value;
            var credential = new ClientSecretCredential(opts.TenantId, opts.ClientId, opts.ClientSecret);
            return new GraphServiceClient(credential, GraphDefaultScopes);
        });

        services.TryAddScoped<IUserStore, EntraUserStore>();
        services.TryAddScoped<IRoleStore, EntraRoleStore>();
        services.TryAddScoped<IAuthenticationFlow, EntraAuthenticationFlow>();

        return services;
    }
}
