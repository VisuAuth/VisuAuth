using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Sample.WebApp.Data;

/// <summary>
/// Seeds a deterministic set of sample users and roles on first run so the
/// admin UI has content to show. Idempotent: running it twice is a no-op.
/// </summary>
public static class UserSeeder
{
    private const string DefaultPassword = "Pa$$w0rd!";

    private static readonly string[] Roles =
    [
        "Admin",
        "Manager",
        "Support",
    ];

    /// <summary>Tenants the sample app demonstrates isolation across.</summary>
    public static readonly string[] Tenants = ["acme", "globex", "initech"];

    private static readonly (string Email, string UserName, string TenantId)[] Users =
    [
        ("admin@visuauth.dev", "admin", "acme"),
        ("alice.silva@example.com", "alice.silva", "acme"),
        ("bruno.costa@example.com", "bruno.costa", "acme"),
        ("carla.dias@example.com", "carla.dias", "acme"),
        ("daniel.eloi@example.com", "daniel.eloi", "globex"),
        ("eduarda.ferraz@example.com", "eduarda.ferraz", "globex"),
        ("fabio.gomes@example.com", "fabio.gomes", "globex"),
        ("gabriela.henriques@example.com", "gabriela.henriques", "globex"),
        ("hugo.iglesias@example.com", "hugo.iglesias", "initech"),
        ("isabela.jorge@example.com", "isabela.jorge", "initech"),
        ("joao.kruger@example.com", "joao.kruger", "initech"),
        ("laura.matos@example.com", "laura.matos", "initech"),
    ];

    private static readonly (string Email, string[] Roles)[] RoleAssignments =
    [
        ("admin@visuauth.dev", new[] { "Admin", "Manager" }),
        ("alice.silva@example.com", new[] { "Manager" }),
        ("bruno.costa@example.com", new[] { "Support" }),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in Roles)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }
            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed role '{roleName}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var (email, userName, tenantId) in Users)
        {
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            // TenantId is set explicitly here so the interceptor (which auto-fills
            // it from HttpContext, absent in seeders) doesn't need a fake context.
            var user = new ApplicationUser
            {
                Email = email,
                UserName = userName,
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
                TenantId = tenantId,
            };

            var result = await userManager.CreateAsync(user, DefaultPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed user '{email}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }

        foreach (var (email, roleNames) in RoleAssignments)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                continue;
            }
            foreach (var roleName in roleNames)
            {
                if (await userManager.IsInRoleAsync(user, roleName))
                {
                    continue;
                }
                var result = await userManager.AddToRoleAsync(user, roleName);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to assign role '{roleName}' to '{email}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}
