using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sample.WebApp.Data;
using Sample.WebApp.Home;
using VisuAuth;
using VisuAuth.Identity.Authentication;
using VisuAuth.Identity.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// SQLite database file lives next to the binaries — zero setup for the sample.
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "visuauth-sample.db");

// Drop-in: one call wires the Identity adapter, the admin UI, and the
// end-user UI.
builder.Services.AddVisuAuth<ApplicationUser>();

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
