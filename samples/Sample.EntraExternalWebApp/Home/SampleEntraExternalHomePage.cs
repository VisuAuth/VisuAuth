using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using VisuAuth.EntraExternal.Configuration;

namespace Sample.EntraExternalWebApp.Home;

/// <summary>
/// Manual-test launcher rendered at <c>/</c>. Lists the VisuAuth URLs a
/// fresh visitor most likely wants to click first, plus a small "health
/// check" footer showing which Entra External tenant the adapter is
/// talking to — useful when bouncing between samples or rotating secrets.
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

    private static IResult HandleAsync(IOptions<EntraExternalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Results.Content(RenderHtml(options.Value), "text/html");
    }

    private static string RenderHtml(EntraExternalOptions options)
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
                footer { margin-top: 2.5rem; padding-top: 1rem; border-top: 1px solid #e5e7eb; font-size: 0.85rem; color: #6b7280; }
                footer dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.25rem 1rem; margin: 0; }
                footer dt { font-weight: 500; }
              </style>
            </head>
            <body>
              <h1>VisuAuth Entra External sample</h1>
              <p class="muted">Minimalist reference app — wires VisuAuth.EntraExternal against a Microsoft Entra External ID tenant (customer identity / CIAM) via Microsoft Graph.</p>
              <ul>
                <li><a href="/visuauth/admin"><code>/visuauth/admin</code></a> &mdash; admin dashboard (capability-driven KPI tiles, customer users from Graph)</li>
                <li><a href="/visuauth/admin/users"><code>/visuauth/admin/users</code></a> &mdash; customer list (identities[]-based emails resolved by the External mapper)</li>
                <li><a href="/visuauth/admin/roles"><code>/visuauth/admin/roles</code></a> &mdash; app roles declared in the registered app's manifest</li>
                <li><a href="/visuauth/login"><code>/visuauth/login</code></a> &mdash; login page (swaps to a Microsoft hint automatically &mdash; SupportsLocalLogin = false on the External capability set; v0.3 PR-C adds the real OIDC redirect)</li>
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
}
