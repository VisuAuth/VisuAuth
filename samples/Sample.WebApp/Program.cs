using VisuAuth;

var builder = WebApplication.CreateBuilder(args);

// Drop-in registration. In a real consumer app, services.AddIdentity<TUser,TRole>()
// + an EF Core DbContext would be configured before this line.
builder.Services.AddVisuAuth();

var app = builder.Build();

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
      <p>Pre-alpha scaffolding. When the real packages land, you'll see:</p>
      <ul>
        <li><a href="/visuauth/login"><code>/visuauth/login</code></a> &mdash; end-user login</li>
        <li><a href="/visuauth/admin"><code>/visuauth/admin</code></a> &mdash; admin dashboard</li>
      </ul>
      <p>For now this app only verifies that the package graph compiles and boots.</p>
    </body>
    </html>
    """, "text/html"));

app.MapVisuAuth();

app.Run();
