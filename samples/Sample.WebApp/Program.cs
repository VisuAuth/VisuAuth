using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sample.WebApp.Data;
using Sample.WebApp.Home;
using Sample.WebApp.Theming;
using VisuAuth;
using VisuAuth.AdminUi.Theming;
using VisuAuth.Identity.Authentication;
using VisuAuth.Identity.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// SQLite database file lives next to the binaries — zero setup for the sample.
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "visuauth-sample.db");

// Drop-in: one call wires the Identity adapter, the admin UI, and the
// end-user UI.
builder.Services.AddVisuAuth<ApplicationUser>();

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

// Sample app turns on the end-user dev mode so password-reset / email
// confirmation tokens render inline (we ship no real email sender here).
// Production consumers leave this off and plug their own IEmailSender.
builder.Services.Configure<VisuAuth.Abstractions.Authentication.EndUserUiOptions>(options =>
{
    options.DevelopmentMode = true;
});

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

// Opt into multi-tenancy. Without this the sample is single-tenant — every
// other VisuAuth feature works the same. The generic overload also wires the
// tenant catalogue store at /visuauth/admin/tenants.
builder.Services.EnableVisuAuthTenancy<AppDbContext, ApplicationUser>(options =>
{
    options.HeaderName = "X-Tenant-Id";
    options.CookieName = "va-tenant";
});

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

var app = builder.Build();

await UserSeeder.SeedAsync(app.Services);

app.UseStaticFiles();
app.UseRouting();
app.UseVisuAuthTenancy();
app.UseAuthentication();
app.UseAuthorization();

// Manual-test launcher at "/" — see Sample.WebApp.Home.SampleHomePage.
app.MapSampleHomePage();

app.MapVisuAuth();

app.Run();

/// <summary>
/// Marker type used by <c>WebApplicationFactory&lt;Program&gt;</c> in tests.
/// </summary>
public partial class Program;
