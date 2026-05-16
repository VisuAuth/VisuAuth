using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sample.WebApp.Data;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.IntegrationTests.EndUser;

/// <summary>
/// Variant of <see cref="VisuAuthTestFactory"/> that registers a fake
/// <c>"TestProvider"</c> external authentication scheme + a test-only
/// <c>/test/external-signin</c> endpoint that stamps the
/// <see cref="IdentityConstants.ExternalScheme"/> cookie with caller-supplied
/// claims. The combination lets integration tests exercise
/// <c>/visuauth/external-login/callback</c> end to end without a real OAuth
/// provider.
/// </summary>
public sealed class ExternalLoginTestFactory : WebApplicationFactory<Program>
{
    /// <summary>Login provider name the fake handler advertises.</summary>
    public const string TestProviderScheme = "TestProvider";

    /// <summary>Display name the login page renders on the button.</summary>
    public const string TestProviderDisplayName = "Test Provider";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"visuauth-extlogin-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseSqlite($"Data Source={_dbPath}");
                options.AddVisuAuthTenancy(sp);
            });

            // Register an empty handler under the TestProvider scheme so it
            // shows up in GetExternalAuthenticationSchemesAsync() and is a
            // valid Challenge target. The handler itself is never asked to
            // do real OAuth — the IStartupFilter below stamps the external
            // cookie directly.
            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, NoOpExternalHandler>(
                    TestProviderScheme,
                    TestProviderDisplayName,
                    _ => { });

            // IStartupFilter rather than builder.Configure so we INSERT
            // middleware into the existing pipeline instead of replacing
            // Program.cs's whole pipeline (which mounts Razor Pages).
            services.AddTransient<IStartupFilter, TestExternalSignInStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // best effort — file lives in %TEMP%, OS reclaims later.
            }
        }
    }

    /// <summary>
    /// Placeholder auth handler — never invoked end-to-end in tests because
    /// the <c>/test/external-signin</c> middleware short-circuits the OAuth
    /// dance. Exists only so the scheme registration is valid.
    /// </summary>
    private sealed class NoOpExternalHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <summary>
    /// Inserts the <c>/test/external-signin</c> short-circuit middleware
    /// EARLY in the pipeline (before authentication / routing) so a test
    /// GET stamps the external cookie + redirects to /external-login/callback
    /// in one shot, identical to what a real OAuth handler would do at the
    /// end of the dance.
    /// </summary>
    private sealed class TestExternalSignInStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (context, n) =>
                {
                    if (context.Request.Path == "/test/external-signin"
                        && HttpMethods.IsGet(context.Request.Method))
                    {
                        var providerKey = context.Request.Query["providerKey"].ToString();
                        var email = context.Request.Query["email"].ToString();
                        var name = context.Request.Query["name"].ToString();
                        var returnUrl = context.Request.Query["returnUrl"].ToString();

                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, providerKey),
                        };
                        if (!string.IsNullOrEmpty(email))
                        {
                            claims.Add(new Claim(ClaimTypes.Email, email));
                        }
                        if (!string.IsNullOrEmpty(name))
                        {
                            claims.Add(new Claim(ClaimTypes.Name, name));
                        }

                        var identity = new ClaimsIdentity(
                            claims,
                            IdentityConstants.ExternalScheme,
                            ClaimTypes.Name,
                            ClaimTypes.Role);
                        var principal = new ClaimsPrincipal(identity);

                        var props = new AuthenticationProperties();
                        // SignInManager.GetExternalLoginInfoAsync reads
                        // `LoginProvider` from the auth properties to populate
                        // ExternalLoginInfo.LoginProvider — same key the real
                        // OAuth callback path uses.
                        props.Items["LoginProvider"] = TestProviderScheme;

                        await context.SignInAsync(IdentityConstants.ExternalScheme, principal, props);

                        var callback = string.IsNullOrEmpty(returnUrl)
                            ? "/visuauth/external-login/callback"
                            : $"/visuauth/external-login/callback?returnUrl={Uri.EscapeDataString(returnUrl)}";
                        context.Response.Redirect(callback);
                        return;
                    }
                    await n();
                });
                next(app);
            };
    }
}
