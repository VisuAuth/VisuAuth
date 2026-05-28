using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using VisuAuth.EntraExternal.Configuration;

namespace Sample.EntraExternalWebApp.Home;

/// <summary>
/// Manual-test launcher rendered at <c>/</c>. Lists the VisuAuth URLs a
/// fresh visitor most likely wants to click first, reflects whether the
/// current request is signed in (so the OIDC round-trip from
/// VisuAuth.EntraExternal.Web is visible without diving into DevTools),
/// plus a small "health check" footer showing which Entra External
/// tenant the adapter is talking to.
/// </summary>
/// <remarks>
/// Pure server-rendered HTML, no framework — keeping the sample
/// dependency-free is part of VisuAuth's drop-in story (CLAUDE.md §2.1).
/// Mirrors the shape of <c>Sample.EntraWebApp.SampleEntraHomePage</c> so
/// a reader hopping between the Workforce and External samples sees the
/// same conventions.
/// </remarks>
internal static class SampleEntraExternalHomePage
{
    /// <summary>
    /// Maps <c>GET /</c> to the launcher. Add new VisuAuth routes here
    /// when they land — same convention as the other samples (see
    /// <c>memory/surface-new-urls-on-sample-home.md</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapSampleEntraExternalHomePage(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/", HandleAsync);
        return endpoints;
    }

    private static IResult HandleAsync(HttpContext http, IOptions<EntraExternalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        return Results.Content(RenderHtml(options.Value, http.User), "text/html");
    }

    private static string RenderHtml(EntraExternalOptions options, ClaimsPrincipal user)
    {
        // Mask the tenant id to the first 8 chars + ellipsis so a casual
        // screenshare doesn't leak the full GUID. The mask is cosmetic
        // (the value is non-secret anyway), but it matches how the
        // /admin pages already truncate ids in the user-summary table.
        var tenantMask = string.IsNullOrEmpty(options.TenantId)
            ? "(not configured)"
            : options.TenantId.Length > 8 ? options.TenantId[..8] + "…" : options.TenantId;
        var domainMask = string.IsNullOrEmpty(options.TenantDomain)
            ? "(not configured)"
            : options.TenantDomain;

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <title>VisuAuth Entra External sample</title>
              <style>
                body { font: 15px/1.5 system-ui, sans-serif; max-width: 720px; margin: 3rem auto; padding: 0 1rem; color: #1f2937; }
                h1 { font-size: 1.5rem; margin: 0 0 0.25rem; }
                code { background: #f3f4f6; padding: 0.05rem 0.35rem; border-radius: 4px; font-size: 0.9em; }
                ul { padding-left: 1.25rem; }
                li { margin: 0.4rem 0; }
                .muted { color: #6b7280; }
                .auth { margin: 1.5rem 0; padding: 0.9rem 1.1rem; border-radius: 8px; border: 1px solid #e5e7eb; }
                .auth.in { background: #ecfdf5; border-color: #a7f3d0; }
                .auth.out { background: #f9fafb; }
                .auth .who { font-weight: 600; }
                .auth a { color: #2563eb; }
                .dot { display: inline-block; width: 0.5rem; height: 0.5rem; border-radius: 50%; margin-right: 0.4rem; vertical-align: middle; }
                .dot.in { background: #10b981; }
                .dot.out { background: #9ca3af; }
                footer { margin-top: 2.5rem; padding-top: 1rem; border-top: 1px solid #e5e7eb; font-size: 0.85rem; color: #6b7280; }
                footer dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.25rem 1rem; margin: 0; }
                footer dt { font-weight: 500; }
              </style>
            </head>
            <body>
              <h1>VisuAuth Entra External sample</h1>
              <p class="muted">Minimalist reference app — wires VisuAuth.EntraExternal against a Microsoft Entra External ID tenant (customer identity / CIAM) via Microsoft Graph.</p>
              {{RenderAuthBlock(user)}}
              <ul>
                <li><a href="/visuauth/admin"><code>/visuauth/admin</code></a> &mdash; admin dashboard (capability-driven KPI tiles, customer users from Graph)</li>
                <li><a href="/visuauth/admin/users"><code>/visuauth/admin/users</code></a> &mdash; customer list (identities[]-based emails resolved by the External mapper)</li>
                <li><a href="/visuauth/admin/roles"><code>/visuauth/admin/roles</code></a> &mdash; app roles declared in the registered app's manifest</li>
                <li><a href="/visuauth/login"><code>/visuauth/login</code></a> &mdash; login page (renders the "Sign in with Microsoft" button wired by VisuAuth.EntraExternal.Web; round-trips through {tenant}.ciamlogin.com)</li>
              </ul>
              <p class="muted">Compare with <code>samples/Sample.EntraWebApp</code> &mdash; same admin UI, Workforce (employees) backend. The capability flag system + the External identities[] mapper are the only things that switch.</p>
              <footer>
                <dl>
                  <dt>Tenant</dt><dd><code>{{tenantMask}}</code></dd>
                  <dt>Tenant domain</dt><dd><code>{{domainMask}}</code></dd>
                  <dt>Graph endpoint</dt><dd><code>{{options.GraphBaseUrl}}</code></dd>
                </dl>
              </footer>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Renders the sign-in status card. Signed-in state is the visible
    /// proof that the VisuAuth.EntraExternal.Web OIDC round-trip landed a
    /// session cookie — the whole point of the PR this sample exercises.
    /// </summary>
    private static string RenderAuthBlock(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated == true)
        {
            var name = ResolveName(user);
            var email = ResolveEmail(user);
            var emailLine = string.IsNullOrEmpty(email) ? string.Empty : $" &mdash; <code>{Escape(email)}</code>";
            return $"""
                <div class="auth in">
                  <span class="dot in"></span><span class="who">Signed in as {Escape(name)}</span>{emailLine}
                  <div class="muted">Session cookie issued by the OIDC round-trip (VisuAuth.EntraExternal.Web). <a href="/visuauth/logout">Sign out</a></div>
                </div>
                """;
        }

        return """
            <div class="auth out">
              <span class="dot out"></span><span class="who">Not signed in</span>
              <div class="muted">Click <a href="/visuauth/login">/visuauth/login</a> and use the "Sign in with Microsoft" button to authenticate against the External tenant.</div>
            </div>
            """;
    }

    private static string ResolveName(ClaimsPrincipal user)
        => user.Identity?.Name
           ?? user.FindFirstValue("name")
           ?? user.FindFirstValue(ClaimTypes.Name)
           ?? "(unknown)";

    private static string? ResolveEmail(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Email)
           ?? user.FindFirstValue("preferred_username");

    /// <summary>
    /// Minimal HTML-encode for the claim values we splice into the page.
    /// Display name / email come from the id_token, which is attacker-
    /// influenceable on a self-service signup tenant — encode so a crafted
    /// display name can't inject markup into this debug launcher.
    /// </summary>
    private static string Escape(string value)
        => System.Net.WebUtility.HtmlEncode(value);
}
