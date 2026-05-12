using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Sample.WebApp.Data;

using VisuAuth;
using VisuAuth.Abstractions.Users;
using VisuAuth.Identity.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// SQLite database file lives next to the binaries — zero setup for the sample.
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "visuauth-sample.db");

// Drop-in: one call wires the Identity adapter, the admin UI, and the
// end-user UI (the last one is stub-only until the next PR).
builder.Services.AddVisuAuth<ApplicationUser>();

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

// User unique-email constraint is relaxed here because the sample seeds the
// same emails across different tenants (alice.silva@... in 'acme' for example
// would conflict with another 'acme' alice on insert — different tenants is
// still fine because TenantId is part of the constraint, but Identity itself
// does not know that yet without a migration). Sample seeder uses unique emails
// per user so this is safe.
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

// Manual-test launcher: every URL VisuAuth currently exposes is linked here so
// the owner can click through and verify a PR end to end. Parameterised routes
// resolve a real id from the store, so the link is clickable rather than a
// `{id}` placeholder.
app.MapGet("/", async (IUserStore userStore, CancellationToken cancellationToken) =>
{
    var page = await userStore.ListAsync(
        new UserFilter { Page = 1, PageSize = 1, SortBy = UserSortBy.Email },
        cancellationToken);
    var firstUser = page.Items.Count > 0 ? page.Items[0] : null;

    var detailHref = firstUser is null
        ? "/visuauth/admin/users/{id}"
        : $"/visuauth/admin/users/{firstUser.Id}";
    var detailLabel = firstUser is null
        ? "user detail (no seeded users yet)"
        : $"user detail &mdash; {System.Net.WebUtility.HtmlEncode(firstUser.Email)}";
    var detailClickable = firstUser is not null;

    var detailLine = detailClickable
        ? $"""<li><a href="{detailHref}"><code>{detailHref}</code></a> &mdash; {detailLabel}</li>"""
        : $"""<li><code>{detailHref}</code> &mdash; {detailLabel}</li>""";

    return Results.Content($$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>VisuAuth sample app</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 760px; margin: 4rem auto; padding: 0 1rem; }
            code { background: #f1f5f9; padding: 0.15rem 0.4rem; border-radius: 0.25rem; }
            a { color: #6366f1; }
            h2 { margin-top: 2rem; font-size: 1.05rem; color: #475569; text-transform: uppercase; letter-spacing: 0.04em; }
            ul { line-height: 1.9; }
          </style>
        </head>
        <body>
          <h1>VisuAuth sample app</h1>
          <p>
            Manual-test launcher for the VisuAuth drop-in admin UI. Every URL the
            library currently exposes is linked below.
          </p>

          <h2>Admin UI</h2>
          <ul>
            <li><a href="/visuauth/admin/users"><code>/visuauth/admin/users</code></a> &mdash; users list (search + pagination)</li>
            <li><a href="/visuauth/admin/users/new"><code>/visuauth/admin/users/new</code></a> &mdash; create user form</li>
            {{detailLine}}
            <li><a href="/visuauth/admin/roles"><code>/visuauth/admin/roles</code></a> &mdash; roles catalogue (member counts, inline create / rename / delete)</li>
            <li><a href="/visuauth/admin/tenants"><code>/visuauth/admin/tenants</code></a> &mdash; tenants catalogue (member counts, inline create / rename / delete)</li>
          </ul>

          <h2>Multi-tenancy</h2>
          <p>
            The sample seeds users across three tenants: <code>acme</code>,
            <code>globex</code>, <code>initech</code>. Pick a tenant in the
            sidebar dropdown to set the scope cookie — every other admin
            view will filter to that tenant's users. APIs can also scope by
            sending the <code>X-Tenant-Id</code> header (header beats cookie).
            "(global)" in the dropdown clears the cookie.
          </p>
          <pre style="background:#f1f5f9;padding:0.75rem;border-radius:0.5rem;overflow:auto;">curl -H "X-Tenant-Id: acme" http://localhost:5239/visuauth/admin/users</pre>

          <h2>End-user UI</h2>
          <ul>
            <li><em>No end-user pages shipped yet &mdash; coming in <code>feat/end-user-login-page</code>.</em></li>
          </ul>

          <h2>Mobile / API</h2>
          <ul>
            <li><em>No mobile endpoints shipped yet &mdash; coming in <code>feat/mobile-rest-api-and-jwt</code>.</em></li>
          </ul>
        </body>
        </html>
        """, "text/html");
});

app.MapVisuAuth();

app.Run();

/// <summary>
/// Marker type used by <c>WebApplicationFactory&lt;Program&gt;</c> in tests.
/// </summary>
public partial class Program;
