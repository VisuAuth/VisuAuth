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
                <li><a href="/visuauth/two-factor/setup"><code>/visuauth/two-factor/setup</code></a> &mdash; pair an authenticator app (auth required)</li>
                <li><a href="/visuauth/two-factor/verify"><code>/visuauth/two-factor/verify</code></a> &mdash; TOTP / recovery-code challenge after a 2FA-required sign-in</li>
                <li><a href="/visuauth/two-factor/recovery-codes"><code>/visuauth/two-factor/recovery-codes</code></a> &mdash; manage recovery codes / disable 2FA (auth required)</li>
                <li><code>/visuauth/external-login/start</code> &mdash; POST-only OAuth kickoff (one form per registered provider on /login)</li>
                <li><code>/visuauth/external-login/callback</code> &mdash; OAuth landing target; signs in or hands off to /confirm</li>
                <li><a href="/visuauth/external-login/confirm"><code>/visuauth/external-login/confirm</code></a> &mdash; new-account form (only used by the AutoLinkByEmailOrConfirm + AlwaysConfirm strategies)</li>
                <li><a href="/visuauth/logout"><code>/visuauth/logout</code></a> &mdash; sign-out endpoint (POST-only confirmation)</li>
              </ul>

              <h2>External login providers</h2>
              <p>
                Buttons appear on <a href="/visuauth/login"><code>/visuauth/login</code></a>
                automatically for every authentication scheme the host registers
                (Google, Microsoft, Apple, …). The sample wires Microsoft
                conditionally: it only registers when
                <code>ExternalProviders.Microsoft.ClientId</code> +
                <code>ExternalProviders.Microsoft.ClientSecret</code> are set
                in any <code>IConfiguration</code> source (appsettings, env
                vars, <strong>user-secrets</strong> &mdash; the recommended
                local option since it stays out of git). The expected shape
                ships as empty placeholders in
                <code>samples/Sample.WebApp/appsettings.json</code>:
              </p>
              <pre style="background:#f1f5f9;padding:0.75rem;border-radius:0.5rem;overflow:auto;">"ExternalProviders": {
                "Microsoft": {
                  "ClientId":     "",
                  "ClientSecret": ""
                }
                // add Google / Apple / GitHub here, then wire them in Program.cs
              }</pre>
              <p>To turn Microsoft on:</p>
              <ol>
                <li>Register an app at <a href="https://entra.microsoft.com/">entra.microsoft.com</a> &rarr; App registrations.</li>
                <li>Under <em>Authentication</em>, add Web redirect URIs for <code>http://localhost:5239/signin-microsoft</code> AND <code>https://localhost:7239/signin-microsoft</code> (the http/https launch profiles &mdash; AddMicrosoftAccount default callback path).</li>
                <li>Under <em>Certificates &amp; secrets</em>, generate a new client secret and copy the <strong>Value</strong> column (not the Secret ID GUID) immediately.</li>
                <li>From <code>samples/Sample.WebApp</code>:
                  <ul>
                    <li><code>dotnet user-secrets set "ExternalProviders:Microsoft:ClientId" "&lt;your-client-id&gt;"</code></li>
                    <li><code>dotnet user-secrets set "ExternalProviders:Microsoft:ClientSecret" "&lt;your-secret-value&gt;"</code></li>
                  </ul>
                </li>
                <li>Restart the sample. The "Continue with Microsoft" button shows up below the password form.</li>
              </ol>
              <p>
                The same keys can also live in <code>appsettings.Development.json</code>
                (gitignored locally if you choose) or environment variables prefixed
                like <code>ExternalProviders__Microsoft__ClientId</code> &mdash;
                whatever is convenient for your environment. In production, lean
                on a real secret store (Azure Key Vault, AWS Secrets Manager,
                etc.) through ASP.NET Core's configuration providers.
              </p>
              <p>
                The first-time strategy (<code>ExternalLoginOptions.FirstTimeStrategy</code>)
                controls what happens when the provider's identity has no linked
                local user. Three options:
              </p>
              <ul>
                <li><code>AutoCreate</code> (sample default) &mdash; provisions a local user from the provider's claims and signs in. Frictionless.</li>
                <li><code>AutoLinkByEmailOrConfirm</code> &mdash; if an existing user owns the provider's email, link automatically. Otherwise show <code>/external-login/confirm</code>.</li>
                <li><code>AlwaysConfirm</code> &mdash; always show <code>/external-login/confirm</code>. Maximum control, max friction.</li>
              </ul>

              <h2>Two-factor sandbox</h2>
              <p>
                <code>twofactor.demo@example.com</code> ships with 2FA pre-enabled
                so the challenge page is reachable without first running setup.
                Pair an authenticator app with the seeded shared key to get rotating
                codes:
              </p>
              <ul>
                <li>Account label: <code>twofactor.demo@example.com</code></li>
                <li>Issuer: <code>VisuAuth.Sample</code></li>
                <li>Shared key (Base32): <code>JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP</code></li>
              </ul>
              <p>
                Sign in as the 2FA demo user (<code>Pa$$w0rd!</code>) at
                <a href="/visuauth/login"><code>/visuauth/login</code></a> &mdash; the
                form will redirect to <code>/visuauth/two-factor/verify</code>. The
                "Use a recovery code instead" disclosure accepts any of the seeded
                recovery codes below (each one is one-shot; once used it stops working
                until you re-seed by deleting <code>visuauth-sample.db</code>):
              </p>
              <ul>
                <li><code>demo1-aaaaa</code></li>
                <li><code>demo2-bbbbb</code></li>
                <li><code>demo3-ccccc</code></li>
              </ul>
              <p>
                <strong>If your authenticator app says the code is invalid</strong>,
                check that your phone clock is in sync with this machine — TOTP
                codes have a ~30 s validity window, so even a 90 s drift between
                client and server makes every code wrong. Either fix the clock or
                use one of the recovery codes above.
              </p>

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

              <h2>Theming</h2>
              <p>
                <code>services.Configure&lt;VisuAuthTheme&gt;(…)</code> overrides the
                default CSS custom properties at runtime — no need to fork the
                stylesheet. To preview different palettes, swap the preset call
                in <code>Program.cs</code> for any method in
                <code>Sample.WebApp.Theming.SampleThemes</code>:
              </p>
              <ul>
                <li><code>Default</code> &mdash; stock indigo, no overrides emitted</li>
                <li><code>Purple</code> &mdash; primary-only override (lightest)</li>
                <li><code>Orange</code> &mdash; warm palette with matching neutrals</li>
                <li><code>Forest</code> &mdash; green primary + coherent success badges</li>
                <li><code>Midnight</code> &mdash; full dark theme (bg / fg / surface flipped)</li>
                <li><code>Serif</code> &mdash; shape + typography only, keeps colours</li>
              </ul>
              <p>
                Inspect the page source for <code>&lt;style data-visuauth-theme&gt;</code>
                to confirm the override block; when the preset is <code>Default</code>
                the tag helper suppresses itself and that block is absent.
              </p>

              <h2>Localization</h2>
              <p>
                The admin and end-user UIs ship with English (default) and
                Brazilian Portuguese (<code>pt-BR</code>). The request culture
                resolves from (in order) <code>?culture=…</code>, the
                <code>.AspNetCore.Culture</code> cookie, and the browser's
                <code>Accept-Language</code> header.
              </p>
              <ul>
                <li>
                  <a href="/visuauth/admin/users?culture=pt-BR"><code>/visuauth/admin/users?culture=pt-BR</code></a>
                  &mdash; force pt-BR on the admin
                </li>
                <li>
                  <a href="/visuauth/login?culture=pt-BR"><code>/visuauth/login?culture=pt-BR</code></a>
                  &mdash; force pt-BR on sign-in
                </li>
                <li>
                  <a href="/visuauth/admin/users?culture=en"><code>?culture=en</code></a>
                  &mdash; back to English
                </li>
              </ul>
              <p>
                The sidebar (admin) and card footer (end-user) also expose a
                language dropdown that posts to <code>/visuauth/culture</code>
                and persists the choice in the cookie. Translations live as
                plain JSON next to the binaries
                (<code>Resources/AdminSharedResources.{culture}.json</code> /
                <code>Resources/EndUserSharedResources.{culture}.json</code>) and
                ship through <code>My.Extensions.Localization.Json</code> behind
                the standard <code>IStringLocalizer&lt;T&gt;</code> contract,
                so swapping the storage backend later does not touch any view.
              </p>

              <h2>WebView deep-link callback</h2>
              <p>
                Native apps can open <code>/visuauth/login</code> in an in-app browser with a
                custom-scheme <code>returnUrl</code>. After sign-in the server mints a JWT
                and redirects to the callback URL with the token in the fragment.
                This sample allows the <code>visuauth-sample</code> scheme; production
                deployments configure their own via
                <code>WebViewCallbackOptions.AllowedSchemes</code>.
              </p>
              <ul>
                <li>
                  Open in your browser:
                  <a href="/visuauth/login?returnUrl=visuauth-sample%3A%2F%2Fauth%2Fcallback">
                    <code>/visuauth/login?returnUrl=visuauth-sample://auth/callback</code>
                  </a>
                  &mdash; after sign-in your browser will try to follow the deep link
                  (and fail, since <code>visuauth-sample://</code> is not registered as
                  a handler on a desktop). Inspect the DevTools Network tab to see
                  <code>Location: visuauth-sample://auth/callback#access_token=&hellip;</code>.
                </li>
              </ul>
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
