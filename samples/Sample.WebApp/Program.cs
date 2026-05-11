using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Sample.WebApp.Data;

using VisuAuth;

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

app.MapGet("/", () => Results.Content("""
    <!doctype html>
    <html lang="en">
    <head>
      <meta charset="utf-8" />
      <title>VisuAuth sample app</title>
      <style>
        body { font-family: system-ui, sans-serif; max-width: 720px; margin: 4rem auto; padding: 0 1rem; }
        code { background: #f1f5f9; padding: 0.15rem 0.4rem; border-radius: 0.25rem; }
        a { color: #6366f1; }
      </style>
    </head>
    <body>
      <h1>VisuAuth sample app</h1>
      <p>Sample app for VisuAuth's drop-in admin UI. Try:</p>
      <ul>
        <li><a href="/visuauth/admin/users"><code>/visuauth/admin/users</code></a> &mdash; admin dashboard (users list)</li>
      </ul>
    </body>
    </html>
    """, "text/html"));

app.MapVisuAuth();

app.Run();

/// <summary>
/// Marker type used by <c>WebApplicationFactory&lt;Program&gt;</c> in tests.
/// </summary>
public partial class Program;
