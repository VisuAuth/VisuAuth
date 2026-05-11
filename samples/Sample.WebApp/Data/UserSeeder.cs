using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Sample.WebApp.Data;

/// <summary>
/// Seeds a deterministic set of sample users on first run so the admin UI has
/// content to show. Idempotent: running it twice is a no-op.
/// </summary>
public static class UserSeeder
{
    private const string DefaultPassword = "Pa$$w0rd!";

    private static readonly (string Email, string UserName)[] Users =
    [
        ("admin@visuauth.dev", "admin"),
        ("alice.silva@example.com", "alice.silva"),
        ("bruno.costa@example.com", "bruno.costa"),
        ("carla.dias@example.com", "carla.dias"),
        ("daniel.eloi@example.com", "daniel.eloi"),
        ("eduarda.ferraz@example.com", "eduarda.ferraz"),
        ("fabio.gomes@example.com", "fabio.gomes"),
        ("gabriela.henriques@example.com", "gabriela.henriques"),
        ("hugo.iglesias@example.com", "hugo.iglesias"),
        ("isabela.jorge@example.com", "isabela.jorge"),
        ("joao.kruger@example.com", "joao.kruger"),
        ("laura.matos@example.com", "laura.matos"),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var (email, userName) in Users)
        {
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                Email = email,
                UserName = userName,
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var result = await userManager.CreateAsync(user, DefaultPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed user '{email}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }
    }
}
