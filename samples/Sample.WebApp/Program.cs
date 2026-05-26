using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sample.WebApp.Data;
using Sample.WebApp.Home;
using Sample.WebApp.Theming;
using VisuAuth;
using VisuAuth.AdminUi.Localization;
using VisuAuth.AdminUi.Theming;
using VisuAuth.Entra.DependencyInjection;
using VisuAuth.Identity.Authentication;
using VisuAuth.Identity.DependencyInjection;
using VisuAuth.Identity.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// Pick the user backend. Three accepted sources, in priority order:
//   1. VISUAUTH_BACKEND env var — the shouty SCREAM_CASE convention
//      operators reach for first ("$env:VISUAUTH_BACKEND='entra'").
//   2. VisuAuth:Backend in any IConfiguration source (appsettings,
//      user-secrets, or the double-underscore env var form
//      VisuAuth__Backend which ASP.NET Core auto-maps to the
//      colon-key shape).
//   3. Default: "identity" — ASP.NET Core Identity against the local
//      SQLite DB.
// The "entra" value swaps to the Microsoft Entra ID adapter against
// Microsoft Graph — same admin UI, different IUserStore / IRoleStore /
// IAuthenticationFlow. See README "Entra adapter" section for the
// app-registration steps + the user-secrets snippet
// (VisuAuth:Entra:TenantId / ClientId / ClientSecret).
var backend = Environment.GetEnvironmentVariable("VISUAUTH_BACKEND")
              ?? builder.Configuration["VisuAuth:Backend"]
              ?? "identity";
var isEntra = string.Equals(backend, "entra", StringComparison.OrdinalIgnoreCase);
Console.WriteLine($"[VisuAuth.Sample] Backend selected: {backend} (isEntra={isEntra})");

// SQLite database file lives next to the binaries — zero setup for the sample.
// Only used by the Identity branch.
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "visuauth-sample.db");

// Fluent composition (CLAUDE.md §2.1, §7). The sample exercises the
// chain form so the README example is verifiably real:
//
//   AddVisuAuth()                            → returns IVisuAuthBuilder
//     .UseAspNetIdentity<ApplicationUser>()  → wires user / role stores
//     .EnableMultiTenant<…>(…)               → tenant resolver + catalogue
//     .AddAdminUi()                          → /visuauth/admin Razor Pages
//     .AddEndUserUi();                       → /visuauth/login etc + JWT
//
// The one-liner `services.AddVisuAuth<ApplicationUser>()` is the drop-in
// shortcut and produces an equivalent service graph; consumers who only
// want a subset (e.g. EndUserUi without AdminUi) reach for this chain.
//
// In Entra mode we skip UseAspNetIdentity / EnableMultiTenant — the
// Entra adapter brings its own IUserStore / IRoleStore /
// IAuthenticationFlow against Microsoft Graph, and per-user tenancy isn't
// a concept on the Entra side (the directory itself IS the tenant).
var visuAuthBuilder = builder.Services.AddVisuAuth();
if (!isEntra)
{
    visuAuthBuilder
        .UseAspNetIdentity<ApplicationUser>()
        // Opt into multi-tenancy. Without this the sample is single-tenant —
        // every other VisuAuth feature works the same. The generic overload
        // also wires the tenant catalogue store at /visuauth/admin/tenants.
        .EnableMultiTenant<AppDbContext, ApplicationUser>(options =>
        {
            options.HeaderName = "X-Tenant-Id";
            options.CookieName = "va-tenant";
        });
}
visuAuthBuilder
    .AddAdminUi()
    .AddEndUserUi();

// Programmatic theme override (CLAUDE.md §8.4 layer 2). The preset list
// lives in Sample.WebApp.Theming.SampleThemes — swap the method group
// below to recolour the entire admin + end-user UI without touching
// anything else. Available presets:
//
//   SampleThemes.Default   — stock indigo (no overrides emitted)
//   SampleThemes.Purple    — purple primary only (lightest override)
//   SampleThemes.Orange    — warm orange palette + matching neutrals
//   SampleThemes.Forest    — green palette, keeps success badges coherent
//   SampleThemes.Midnight  — full dark theme (bg / fg / surface flipped)
//   SampleThemes.Serif     — Georgia + larger radius, leaves colours alone
//
// Production consumers replace this with their own brand palette, or drop
// the call entirely to keep the stock theme.
builder.Services.Configure<VisuAuthTheme>(SampleThemes.Purple);

// Theming layer 4 (CLAUDE.md §8.4) — per-tenant overrides. Sample maps
// each seeded tenant id to a different palette so flipping the sidebar
// switcher visibly re-skins the dashboard:
//
//   acme    → Forest (green)
//   globex  → Orange (warm)
//   initech → Midnight (dark)
//   <none>  → falls through to the global SampleThemes.Purple above
//
// Production resolvers typically pull from a tenants table and cache
// the result; this sample uses an in-memory mapping for clarity.
// Comment this line out to keep the global theme on every tenant.
builder.Services.AddSingleton<ITenantThemeResolver, SampleTenantThemeResolver>();

// Per-tenant view overrides (CLAUDE.md §8.4 layers 3+4 composed).
// SampleTenantViewOverrideResolver maps the seeded `acme` tenant to
// /Views/VisuAuth/Tenants/acme/, which holds an acme-branded
// _UsersTable.cshtml. Other tenants fall through to the global
// /Views/VisuAuth/ override (or the package default if neither exists).
//
// Same opt-in story as the theme resolver: comment this line out to
// disable per-tenant view overrides without touching anything else.
builder.Services.AddSingleton<ITenantViewOverrideResolver, SampleTenantViewOverrideResolver>();

// Theming layer 3 (CLAUDE.md §8.4) — view override. The sample app drops
// two demo .cshtml files into `Views/VisuAuth/` and `Views/VisuAuth/Shared/`
// to show how partial + layout overrides plug in without any code change:
//
//   Views/VisuAuth/_UsersTable.cshtml          → replaces the admin users table
//   Views/VisuAuth/Shared/_EndUserLayout.cshtml → replaces the public sign-in layout
//
// The IViewLocationExpander registered by AddVisuAuth() probes /Views/VisuAuth/
// before the package's own templates, so a same-named file wins automatically.
// To use a non-default folder, uncomment:
//
//   builder.Services.Configure<VisuAuthViewOverrideOptions>(o => o.Root = "/Views/MyBrand");
//
// Full-page overrides need no extra config — a consumer Razor Page in the
// host app declaring `@page "/visuauth/login"` (or any other VisuAuth route)
// wins via the lower-order-wins routing rule; the sample skips that demo to
// keep the seeded login flow unchanged.

// Sample app turns on the end-user dev mode so password-reset / email
// confirmation tokens render inline (we ship no real email sender here).
// Production consumers leave this off and plug their own IEmailSender.
builder.Services.Configure<VisuAuth.Abstractions.Authentication.EndUserUiOptions>(options =>
{
    options.DevelopmentMode = true;
});

// TOTP issuer label embedded in the otpauth:// URI rendered as a QR on
// /visuauth/two-factor/setup. Authenticator apps display this above the
// account name; defaulting to the product name keeps multi-app rosters
// readable when the user has many enrolments.
builder.Services.Configure<VisuAuth.Identity.Authentication.TwoFactorIssuerOptions>(options =>
{
    options.Issuer = "VisuAuth.Sample";
});

// JWT issuer is Identity-only — AspNetIdentityJwtIssuer<TUser> needs
// UserManager<TUser>, which only AddIdentity registers. In Entra mode the
// mobile / API channel is satisfied by Microsoft's own tokens, so we skip
// the issuer registration entirely; the AuthApi minimal-API just doesn't
// have an issuer to call.
if (!isEntra)
{
    // Mobile / native API channel: HS256 JWTs at /visuauth/api/auth. The signing
    // key below is committed for dev convenience — a real deployment loads it
    // from a secret store / Key Vault. 32+ UTF-8 bytes is mandatory for HS256.
    builder.Services.AddVisuAuthJwt<ApplicationUser>(options =>
    {
        options.SigningKey = "sample-dev-signing-key-do-not-use-in-production-or-anywhere-else";
        options.Issuer = "VisuAuth.Sample";
        options.Audience = "VisuAuth.Sample";
        options.LifetimeMinutes = 60;
    });
}

// WebView callback flow: a native app opens an in-app browser at
// /visuauth/login?returnUrl=visuauth-sample://auth/callback and receives
// the JWT in the URL fragment after a successful sign-in.
//
// `ShowPreviewPage = true` keeps the desktop developer in the loop —
// instead of a silent redirect to a scheme the OS does not register, the
// page renders a confirmation panel with the callback URL and a Continue
// button. Production deployments should leave this false.
builder.Services.Configure<VisuAuth.Abstractions.Authentication.WebViewCallbackOptions>(options =>
{
    options.AllowedSchemes.Add("visuauth-sample");
    options.ShowPreviewPage = true;
});

if (isEntra)
{
    // Entra branch — registers EntraUserStore / EntraRoleStore /
    // EntraAuthenticationFlow against Microsoft Graph. Reads
    // TenantId / ClientId / ClientSecret from configuration under
    // "VisuAuth:Entra" (typically populated via user-secrets in dev or
    // env vars in production). No DbContext, no Identity, no JWT issuer
    // — Microsoft owns the login UX and the directory entirely.
    builder.Services.AddVisuAuthEntra(builder.Configuration);
}
else
{
    WireIdentityBackend(builder, dbPath);
}

var app = builder.Build();

if (!isEntra)
{
    // UserSeeder pokes at UserManager / RoleManager / AppDbContext — only
    // available in the Identity branch. Entra mode relies on whatever
    // the configured tenant already holds.
    await UserSeeder.SeedAsync(app.Services);
}

app.UseStaticFiles();
// UseVisuAuthLocalization plugs the request-localization middleware into
// the pipeline. Must run before any localized response is rendered.
app.UseVisuAuthLocalization();
app.UseRouting();
if (!isEntra)
{
    // Tenancy middleware reads X-Tenant-Id / cookie — only meaningful
    // when EnableMultiTenant was called.
    app.UseVisuAuthTenancy();
}
app.UseAuthentication();
app.UseAuthorization();

// Manual-test launcher at "/" — see Sample.WebApp.Home.SampleHomePage.
app.MapSampleHomePage();

app.MapVisuAuth();

app.Run();

/// <summary>
/// Wires the ASP.NET Core Identity backend (default sample mode) — local
/// SQLite, AddIdentity, JWT issuer, external OAuth providers, audit log.
/// Extracted so the Entra branch can opt out cleanly. Local function so
/// it lives next to its callsite without leaking helper types.
/// </summary>
static void WireIdentityBackend(WebApplicationBuilder builder, string dbPath)
{
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlite($"Data Source={dbPath}");
    // Wires the TenantSaveChangesInterceptor so new users get their TenantId
    // stamped automatically. No-op in single-tenant deployments.
    options.AddVisuAuthTenancy(sp);
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Sample defaults — relaxed so the seeded password works without ceremony.
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// External-login providers — wired conditionally so the sample runs out of
// the box even without OAuth credentials.
//
// Config keys live under "ExternalProviders.<Provider>" — appsettings.json
// ships empty placeholders so the shape is discoverable, but real values
// come from any IConfiguration source. Recommended local path:
//
//   dotnet user-secrets set "ExternalProviders:Microsoft:ClientId"     "..."
//   dotnet user-secrets set "ExternalProviders:Microsoft:ClientSecret" "..."
//
// user-secrets writes to %APPDATA%\Microsoft\UserSecrets\ — outside the
// repo, never committed. See SampleHomePage.cs / `/` for the full
// per-provider app-registration steps + redirect URIs to register.
//
// NEVER hardcode secrets here — anything in this file ships in source
// control and ends up in git history forever. The IConfiguration
// indirection also makes production rotations / Key Vault swaps trivial.
//
// To add another provider: add a sibling sub-object under
// ExternalProviders in appsettings.json + call AddXxx below the same way.
// VisuAuth's external-login pages pick up whatever schemes are registered.
// All 4 schemes are ALWAYS registered with whatever appsettings /
// user-secrets carries (empty when unset) so the dynamic overlay from
// the admin UI can drive them at runtime — even when configuration has
// nothing yet. Without this pre-reg the admin couldn't enable a provider
// from scratch without a code change.
//
// Validate-safety: VisuAuth's DynamicExternalProviderOptionsConfigurator
// drops a non-empty sentinel into options.ClientId/Secret at the end of
// its Configure pipeline if neither source supplied a value, so
// OAuthOptions.Validate (which throws on empty) doesn't bring the login
// page down before any admin sees it. The snapshot still records the
// genuine pre-overlay state, so the admin UI's "from code" badge only
// lights up when the consumer actually filled something in.
var externalProviders = builder.Configuration.GetSection("ExternalProviders");
var authenticationBuilder = builder.Services.AddAuthentication();

RegisterScheme("Microsoft", (id, secret) =>
    authenticationBuilder.AddMicrosoftAccount(o =>
    {
        o.ClientId = id;
        o.ClientSecret = secret;
    }));

RegisterScheme("Google", (id, secret) =>
    authenticationBuilder.AddGoogle(o =>
    {
        o.ClientId = id;
        o.ClientSecret = secret;
    }));

RegisterScheme("GitHub", (id, secret) =>
    authenticationBuilder.AddGitHub(o =>
    {
        o.ClientId = id;
        o.ClientSecret = secret;
    }));

RegisterScheme("Apple", (id, secret) =>
    authenticationBuilder.AddApple(o =>
    {
        o.ClientId = id;
        // Apple uses a private-key JWT instead of a static client secret
        // for production. For dev/test the generated client secret string
        // works as-is; for real deployments swap to options.GenerateClientSecret
        // with a .p8 key. See the AspNet.Security.OAuth.Apple docs.
        o.ClientSecret = secret;
    }));

// Per-scheme dynamic option overlay — reads admin-edited credentials from
// the EF-backed store and overrides the static defaults above at request
// time. The cache invalidator (also registered here) is what lets the
// admin save take effect without an app restart.
builder.Services.AddVisuAuthExternalProviderConfigStore();
builder.Services.AddVisuAuthDynamicExternalProviderOptions<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>("Microsoft");
builder.Services.AddVisuAuthDynamicExternalProviderOptions<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>("Google");
builder.Services.AddVisuAuthDynamicExternalProviderOptions<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>("GitHub");
builder.Services.AddVisuAuthDynamicExternalProviderOptions<AspNet.Security.OAuth.Apple.AppleAuthenticationOptions>("Apple");

// Audit log plugin (opt-in). Without this call, the default NoOpAuditWriter
// handles every IAuditWriter.WriteAsync at zero cost; with it,
// EfCoreAuditStore kicks in and rows land in the VisuAuthAuditLog table.
// Default RetentionDays = 90 — pass a lambda to override.
builder.Services.AddVisuAuthAuditLog();

void RegisterScheme(string providerName, Action<string, string> register)
{
    // appsettings / user-secrets value when present; empty otherwise.
    // VisuAuth's DynamicExternalProviderOptionsConfigurator drops a sentinel
    // into ClientId/Secret at the end of its Configure pipeline if neither
    // the static path nor the DB overlay supplied a value, so
    // OAuthOptions.Validate stays happy without us shipping a placeholder
    // string through the snapshot (which would light up "from code" badges
    // for providers nobody actually configured).
    var id = externalProviders[$"{providerName}:ClientId"] ?? string.Empty;
    var secret = externalProviders[$"{providerName}:ClientSecret"] ?? string.Empty;
    register(id, secret);
}

// First-time strategy for external sign-ins: defaults to AutoCreate (a fresh
// local user is provisioned from the provider's claims). Swap to
// AutoLinkByEmailOrConfirm or AlwaysConfirm if account creation needs human
// input — see ExternalLoginOptions doc.
builder.Services.Configure<VisuAuth.Abstractions.Authentication.ExternalLoginOptions>(options =>
{
    // This is the default, but set explicitly here for clarity. Change to AutoLinkByEmailOrConfirm or AlwaysConfirm to require user input on first-time external logins.
    options.FirstTimeStrategy = VisuAuth.Abstractions.Authentication.ExternalLoginFirstTimeStrategy.AutoCreate;
});

} // end WireIdentityBackend

/// <summary>
/// Marker type used by <c>WebApplicationFactory&lt;Program&gt;</c> in tests.
/// </summary>
public partial class Program;
