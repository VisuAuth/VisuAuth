using System.Net;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using VisuAuth.Abstractions.Users;

namespace Sample.WebApp.Home;

/// <summary>
/// Manual-test launcher rendered at <c>/</c>. Lists every VisuAuth URL the
/// sample currently exposes so the owner (and anyone tinkering with the
/// repo) can click through and verify a PR end to end. Pure server-rendered
/// HTML, no framework — keeping the sample app dependency-free is part of
/// the drop-in story.
/// </summary>
internal static class SampleHomePage
{
    /// <summary>
    /// Maps <c>GET /</c> to the launcher. Add new VisuAuth routes here when
    /// they land — see <c>memory/surface-new-urls-on-sample-home.md</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapSampleHomePage(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        IUserStore userStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var firstUser = await ResolveFirstUserAsync(userStore, cancellationToken);
        var html = RenderHtml(firstUser, httpContext.User);
        return Results.Content(html, "text/html");
    }

    private static async Task<UserSummary?> ResolveFirstUserAsync(IUserStore userStore, CancellationToken cancellationToken)
    {
        var page = await userStore.ListAsync(
            new UserFilter { Page = 1, PageSize = 1, SortBy = UserSortBy.Email },
            cancellationToken);
        return page.Items.Count > 0 ? page.Items[0] : null;
    }

    private static string RenderHtml(UserSummary? firstUser, System.Security.Claims.ClaimsPrincipal user)
    {
        var detailLine = BuildDetailLine(firstUser);
        var authStatusLine = BuildAuthStatusLine(user);

        var sb = new StringBuilder(2048);
        sb.Append("""
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
            """);
        sb.Append("  ").AppendLine(authStatusLine);

        sb.Append("""

              <h2>Admin UI</h2>
              <ul>
                <li><a href="/visuauth/admin/users"><code>/visuauth/admin/users</code></a> &mdash; users list (search, role / status / verified / 2FA filters, pagination)</li>
                <li><a href="/visuauth/admin/users/new"><code>/visuauth/admin/users/new</code></a> &mdash; create user form</li>
            """);
        sb.Append("    ").AppendLine(detailLine);
        sb.Append("""
                <li><a href="/visuauth/admin/roles"><code>/visuauth/admin/roles</code></a> &mdash; roles catalogue (member counts, inline create / rename / delete)</li>
                <li><a href="/visuauth/admin/tenants"><code>/visuauth/admin/tenants</code></a> &mdash; tenants catalogue (member counts, inline create / rename / delete)</li>
              </ul>

              <h2>End-user UI</h2>
              <ul>
                <li><a href="/visuauth/login"><code>/visuauth/login</code></a> &mdash; sign-in form (email + password + remember-me)</li>
                <li><a href="/visuauth/register"><code>/visuauth/register</code></a> &mdash; self-service registration</li>
                <li><a href="/visuauth/forgot-password"><code>/visuauth/forgot-password</code></a> &mdash; request a password reset (dev mode surfaces the reset link)</li>
                <li><code>/visuauth/reset-password?email=&amp;token=</code> &mdash; reset landing (reached from the link above)</li>
                <li><code>/visuauth/confirm-email?userId=&amp;token=</code> &mdash; email confirmation landing</li>
                <li><a href="/visuauth/logout"><code>/visuauth/logout</code></a> &mdash; sign-out endpoint (POST-only confirmation)</li>
              </ul>

              <h2>Multi-tenancy</h2>
              <p>
                The sample seeds users across three tenants: <code>acme</code>,
                <code>globex</code>, <code>initech</code>. Pick a tenant in the
                sidebar dropdown to set the scope cookie &mdash; every other admin
                view will filter to that tenant's users. APIs can also scope by
                sending the <code>X-Tenant-Id</code> header (header beats cookie).
                "(global)" in the dropdown clears the cookie.
              </p>
              <pre style="background:#f1f5f9;padding:0.75rem;border-radius:0.5rem;overflow:auto;">curl -H "X-Tenant-Id: acme" http://localhost:5239/visuauth/admin/users</pre>

              <h2>Mobile / API</h2>
              <p>
                JWT-issuing REST endpoints for native / mobile clients. HS256,
                1-hour tokens, claims include <code>sub</code>, <code>email</code>,
                <code>tenant_id</code>, and <code>roles</code>.
              </p>
              <ul>
                <li><code>POST /visuauth/api/auth/login</code> &mdash; <code>{ email, password }</code> &rArr; JWT</li>
                <li><code>POST /visuauth/api/auth/register</code> &mdash; same body, also auto-logs in</li>
                <li><code>POST /visuauth/api/auth/refresh</code> &mdash; <code>Authorization: Bearer &lt;old token&gt;</code> &rArr; new JWT</li>
              </ul>
              <pre style="background:#f1f5f9;padding:0.75rem;border-radius:0.5rem;overflow:auto;">curl -X POST http://localhost:5239/visuauth/api/auth/login \
              -H "Content-Type: application/json" \
              -d '{"email":"admin@visuauth.dev","password":"Pa$$w0rd!"}'</pre>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    private static string BuildDetailLine(UserSummary? firstUser)
    {
        if (firstUser is null)
        {
            const string placeholder = "/visuauth/admin/users/{id}";
            return $"""<li><code>{placeholder}</code> &mdash; user detail (no seeded users yet)</li>""";
        }

        var href = $"/visuauth/admin/users/{firstUser.Id}";
        var label = $"user detail &mdash; {WebUtility.HtmlEncode(firstUser.Email)}";
        return $"""<li><a href="{href}"><code>{href}</code></a> &mdash; {label}</li>""";
    }

    private static string BuildAuthStatusLine(System.Security.Claims.ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return """<p>Currently <strong>signed out</strong>. <a href="/visuauth/login">Sign in</a> to test the end-user flow (seeded password: <code>Pa$$w0rd!</code>).</p>""";
        }

        var name = WebUtility.HtmlEncode(user.Identity.Name ?? "(unknown)");
        return $"""<p>Currently signed in as <strong>{name}</strong>. <a href="/visuauth/logout">Sign out</a>.</p>""";
    }
}
