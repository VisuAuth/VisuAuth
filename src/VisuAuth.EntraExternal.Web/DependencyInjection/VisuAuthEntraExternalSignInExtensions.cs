using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.EntraExternal.Web.Configuration;

namespace VisuAuth.EntraExternal.Web.DependencyInjection;

/// <summary>
/// Composition root for the End-user OIDC sign-in surface of the
/// VisuAuth Entra External adapter. Wraps
/// <see cref="MicrosoftIdentityWebAppAuthenticationBuilderExtensions.AddMicrosoftIdentityWebApp"/>
/// with VisuAuth defaults and replaces the no-op
/// <see cref="IExternalLoginFlow"/> stub the EntraCore package registers
/// with a real implementation backed by the Microsoft.Identity.Web
/// principal.
/// </summary>
/// <remarks>
/// <para>
/// Call this AFTER <c>AddVisuAuthEntraExternal(...)</c> so the External
/// adapter's <see cref="VisuAuth.Abstractions.Users.IUserStore"/>
/// is in DI by the time the real flow is wired (it needs the store to
/// verify the OIDC-authenticated user against Graph).
/// </para>
/// <para>
/// Typical wiring in the consumer's <c>Program.cs</c>:
/// <code>
/// builder.Services
///     .AddVisuAuth()
///     .AddAdminUi()
///     .AddEndUserUi();
///
/// builder.Services.AddVisuAuthEntraExternal(builder.Configuration);
/// builder.Services.AddVisuAuthEntraExternalSignIn(builder.Configuration);
///
/// var app = builder.Build();
/// app.UseStaticFiles();
/// app.UseRouting();
/// app.UseAuthentication();
/// app.UseAuthorization();
/// app.MapVisuAuth();
/// app.Run();
/// </code>
/// </para>
/// </remarks>
public static class VisuAuthEntraExternalSignInExtensions
{
    /// <summary>Default configuration section bound to <see cref="EntraExternalWebOptions"/>.</summary>
    public const string DefaultConfigurationSection = "VisuAuth:EntraExternal:Web";

    /// <summary>
    /// Wires the End-user OIDC sign-in surface from a configuration section.
    /// Defaults to <see cref="DefaultConfigurationSection"/>
    /// (<c>VisuAuth:EntraExternal:Web</c>).
    /// </summary>
    public static IServiceCollection AddVisuAuthEntraExternalSignIn(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName);
        services
            .AddOptions<EntraExternalWebOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new EntraExternalWebOptions();
        section.Bind(options);
        return RegisterCore(services, options);
    }

    /// <summary>
    /// Wires the End-user OIDC sign-in surface from an inline configuration
    /// lambda — shortest path when values come from code, environment
    /// variables, or a custom resolver rather than <c>appsettings.json</c>.
    /// </summary>
    public static IServiceCollection AddVisuAuthEntraExternalSignIn(
        this IServiceCollection services,
        Action<EntraExternalWebOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<EntraExternalWebOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new EntraExternalWebOptions();
        configure(options);
        return RegisterCore(services, options);
    }

    private static IServiceCollection RegisterCore(IServiceCollection services, EntraExternalWebOptions options)
    {
        // Microsoft.Identity.Web's pipeline: register the OIDC handler
        // under a VisuAuth-stable scheme name, configure it against the
        // External tenant authority + the consumer app's redirect URIs.
        // The OIDC scheme is what /visuauth/external-login/start
        // challenges — the constant on EntraExternalLoginFlow is what
        // GetProvidersAsync returns, so both halves must agree.
        services
            .AddAuthentication(EntraExternalLoginFlow.ProviderScheme)
            .AddMicrosoftIdentityWebApp(
                msIdent =>
                {
                    msIdent.Instance = $"https://{options.TenantSubdomain}.ciamlogin.com/";
                    msIdent.TenantId = options.TenantId;
                    msIdent.ClientId = options.ClientId;
                    msIdent.ClientSecret = options.ClientSecret;
                    msIdent.CallbackPath = options.CallbackPath;
                    msIdent.SignedOutCallbackPath = options.SignedOutCallbackPath;
                    msIdent.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    // External tenants don't use the v2.0-suffixed metadata
                    // URL the Workforce defaults assume; let
                    // Microsoft.Identity.Web build the right one from
                    // Instance + TenantId.
                    msIdent.SignedOutRedirectUri = "/";
                },
                openIdConnectScheme: EntraExternalLoginFlow.ProviderScheme,
                cookieScheme: CookieAuthenticationDefaults.AuthenticationScheme,
                subscribeToOpenIdConnectMiddlewareDiagnosticsEvents: false);

        // The Cookies handler is the local session store — Microsoft.Identity.Web
        // wires it implicitly, but make the scheme explicit here so the
        // assertion in EntraExternalLoginFlow that the principal is on
        // that scheme stays true even if the upstream defaults change.
        services.PostConfigure<OpenIdConnectOptions>(EntraExternalLoginFlow.ProviderScheme, oidc =>
        {
            oidc.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            // Force the authorization-code flow. Entra External ID does NOT
            // enable the implicit / hybrid `id_token` response type on app
            // registrations by default (Microsoft's CIAM guidance is
            // code-flow + PKCE only), so the OpenIdConnect handler's hybrid
            // default ("code id_token") fails the authorize request with
            // AADSTS700054 "response_type 'id_token' is not enabled for the
            // application". Pinning ResponseType = code means the id_token
            // comes back from the token endpoint (exchanged with the client
            // secret we already configure) instead of the front channel —
            // the secure, recommended path, and it works against a fresh
            // External tenant with no extra portal toggle.
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            // Map the "name" claim so HttpContext.User.Identity.Name reads
            // naturally on the End-user UI. Identity.Web's default is the
            // long URI form; the short name is friendlier downstream.
            oidc.TokenValidationParameters.NameClaimType = "name";
        });

        // IHttpContextAccessor is what EntraExternalLoginFlow reads the
        // authenticated principal off of. Standard registration; TryAdd
        // because the consumer's app might already have it.
        services.AddHttpContextAccessor();

        // Replace the EntraCore no-op IExternalLoginFlow with the real
        // implementation. Replace (not TryAddScoped) is the right verb
        // here — the no-op is a deliberately weak placeholder that
        // shouldn't survive once we have a real one.
        services.Replace(
            ServiceDescriptor.Scoped<IExternalLoginFlow, EntraExternalLoginFlow>());

        return services;
    }
}
