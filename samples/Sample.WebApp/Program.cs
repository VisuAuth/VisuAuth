using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Sample.WebApp.Data;

using VisuAuth;
using VisuAuth.Abstractions.Users;

var builder = WebApplication.CreateBuilder(args);

// SQLite database file lives next to the binaries — zero setup for the sample.
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "visuauth-sample.db");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

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

// Drop-in: one call wires the Identity adapter, the admin UI, and the
// end-user UI (the last one is stub-only until the next PR).
builder.Services.AddVisuAuth<ApplicationUser>();

var app = builder.Build();

await UserSeeder.SeedAsync(app.Services);

app.UseStaticFiles();
app.UseRouting();

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
            <li><a href="/visuauth/admin/roles"><code>/visuauth/admin/roles</code></a> &mdash; roles catalogue (member counts, inline create / delete)</li>
          </ul>

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
