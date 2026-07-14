using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sample.WebApp.Data;
using VisuAuth.AdminUi.DependencyInjection;
using VisuAuth.Identity.MultiTenancy;

namespace VisuAuth.IntegrationTests;

/// <summary>
/// WebApplicationFactory that redirects <see cref="AppDbContext"/> to a
/// fresh per-instance SQLite file under <see cref="Path.GetTempPath"/>. Each
/// <c>IClassFixture&lt;VisuAuthTestFactory&gt;</c> gets its own isolated
/// database, seeded from scratch by <see cref="UserSeeder"/>; nothing
/// survives between test runs.
/// </summary>
public class VisuAuthTestFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"visuauth-tests-{Guid.NewGuid():N}.db");

    /// <summary>
    /// When <c>true</c> (default) the admin authorization gate is relaxed to
    /// allow-all so page-behaviour tests can hit <c>/visuauth/admin/*</c>
    /// without wiring a sign-in. The dedicated authorization tests derive a
    /// factory that overrides this to <c>false</c> to exercise the real gate.
    /// </summary>
    protected virtual bool RelaxAdminAuthorization => true;

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

            if (RelaxAdminAuthorization)
            {
                // The admin dashboard is secured by default; the bulk of the
                // admin tests target page behaviour, not the gate, so open it
                // up here. VisuAuthAdminAuthorizationTests uses a factory that
                // keeps the real policy.
                services.PostConfigure<AuthorizationOptions>(options =>
                    options.AddPolicy(
                        VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy,
                        policy => policy.RequireAssertion(_ => true)));
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // SqliteConnection pools handles per connection string; until we drop
        // them the .db / -shm / -wal sidecars stay locked on Windows.
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
                // The file lives under %TEMP% so the OS reclaims it eventually.
            }
        }
    }
}
