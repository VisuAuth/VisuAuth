using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VisuAuth.Entra.Web.Configuration;

namespace VisuAuth.Entra.Web.DependencyInjection;

/// <summary>
/// Adds operator sign-in to the VisuAuth Entra ID (Workforce) adapter, so the
/// admin dashboard can challenge through the tenant's hosted Microsoft page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this package exists.</b> <c>AddVisuAuthEntra</c> wires Graph with
/// <em>app-only</em> credentials — that authenticates the app to Microsoft, not
/// a human to the app. On its own it registers no authentication scheme at all,
/// so a protected <c>/visuauth/admin</c> has nothing to challenge with and the
/// operator has no way in. This call closes that gap.
/// </para>
/// <para>
/// Typical wiring:
/// <code>
/// builder.Services.AddVisuAuth().AddAdminUi();
/// builder.Services.AddVisuAuthEntra(builder.Configuration);
/// builder.Services.AddVisuAuthEntraSignIn(builder.Configuration);
///
/// var app = builder.Build();
/// app.UseAuthentication();
/// app.UseAuthorization();
/// app.MapVisuAuth();
/// </code>
/// Restrict the dashboard to a role by registering a policy named
/// <c>VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy</c>;
/// map the app roles from your registration onto it.
/// </para>
/// </remarks>
public static class VisuAuthEntraSignInExtensions
{
    /// <summary>Default configuration section bound to <see cref="EntraWebOptions"/>.</summary>
    public const string DefaultConfigurationSection = "VisuAuth:Entra:Web";

    /// <summary>
    /// The OIDC scheme VisuAuth registers the Entra sign-in handler under.
    /// Stable so consumers can reference it in their own policies.
    /// </summary>
    public const string SignInScheme = "VisuAuth.Entra.OpenIdConnect";

    /// <summary>
    /// Wires operator sign-in from a configuration section. Defaults to
    /// <see cref="DefaultConfigurationSection"/> (<c>VisuAuth:Entra:Web</c>).
    /// </summary>
    public static IServiceCollection AddVisuAuthEntraSignIn(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName);
        services
            .AddOptions<EntraWebOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new EntraWebOptions();
        section.Bind(options);
        return RegisterCore(services, options);
    }

    /// <summary>
    /// Wires operator sign-in from an inline configuration lambda — shortest
    /// path when values come from code or environment variables.
    /// </summary>
    public static IServiceCollection AddVisuAuthEntraSignIn(
        this IServiceCollection services,
        Action<EntraWebOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<EntraWebOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new EntraWebOptions();
        configure(options);
        return RegisterCore(services, options);
    }

    private static IServiceCollection RegisterCore(IServiceCollection services, EntraWebOptions options)
    {
        // The two default schemes are set explicitly rather than via
        // AddAuthentication(oidcScheme), because they play different roles and
        // conflating them is the classic redirect-loop bug: the cookie is what
        // authenticates an established session on every request, while OIDC is
        // only ever used to *start* one. Being explicit also means
        // DefaultChallengeScheme is unambiguously non-null, which is exactly
        // what the admin authorization gate needs to challenge instead of
        // throwing "No authenticationScheme was specified".
        services
            .AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultChallengeScheme = SignInScheme;
            })
            .AddMicrosoftIdentityWebApp(
                msIdent =>
                {
                    msIdent.Instance = options.Instance;
                    msIdent.TenantId = options.TenantId;
                    msIdent.ClientId = options.ClientId;
                    msIdent.ClientSecret = options.ClientSecret;
                    msIdent.CallbackPath = options.CallbackPath;
                    msIdent.SignedOutCallbackPath = options.SignedOutCallbackPath;
                    msIdent.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    msIdent.SignedOutRedirectUri = "/";
                },
                openIdConnectScheme: SignInScheme,
                cookieScheme: CookieAuthenticationDefaults.AuthenticationScheme,
                subscribeToOpenIdConnectMiddlewareDiagnosticsEvents: false);

        services.PostConfigure<OpenIdConnectOptions>(SignInScheme, oidc =>
        {
            oidc.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            // Authorization-code flow: the id_token comes back from the token
            // endpoint rather than the front channel. The hybrid default
            // ("code id_token") needs the implicit grant toggled on the app
            // registration, which fresh Workforce registrations do not have.
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            // Short claim name so HttpContext.User.Identity.Name reads
            // naturally in the admin layout.
            oidc.TokenValidationParameters.NameClaimType = "name";
        });

        services.AddHttpContextAccessor();

        return services;
    }
}
