using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.MultiTenancy;

namespace Sample.WebApp.Data;

/// <summary>
/// Seeds a deterministic set of sample users and roles on first run so the
/// admin UI has content to show. Idempotent: running it twice is a no-op.
/// </summary>
public static class UserSeeder
{
    private const string DefaultPassword = "Pa$$w0rd!";

    /// <summary>
    /// Email of the seeded user that boots with 2FA enabled. Dedicated rather
    /// than reusing an existing user so existing tests that exercise the
    /// other accounts (e.g. WebView callback, JWT API, mutation flows) keep
    /// working unchanged.
    /// </summary>
    public const string TwoFactorEnabledUserEmail = "twofactor.demo@example.com";

    /// <summary>
    /// Deterministic authenticator shared key the seeder enrols on
    /// <see cref="TwoFactorEnabledUserEmail"/>. Base32, 32 chars (160 bits) —
    /// the canonical RFC 6238 recommendation. Pair an authenticator app with
    /// this key + the user's email to drive the challenge flow manually.
    /// </summary>
    public const string TwoFactorEnabledUserKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    /// <summary>
    /// Deterministic recovery codes seeded alongside the 2FA enrolment so
    /// the recovery-code path on <c>/visuauth/two-factor/verify</c> is
    /// exercisable from the sample without first generating a batch on the
    /// recovery-codes page. Identity stores them verbatim (string compare);
    /// the readable shape below avoids the user having to copy random strings
    /// when testing the flow manually.
    /// </summary>
    public static readonly IReadOnlyList<string> TwoFactorEnabledUserRecoveryCodes =
    [
        "demo1-aaaaa",
        "demo2-bbbbb",
        "demo3-ccccc",
    ];

    private static readonly string[] Roles =
    [
        "Admin",
        "Manager",
        "Support",
    ];

    /// <summary>Tenants the sample app demonstrates isolation across.</summary>
    private static readonly (string Id, string DisplayName)[] Tenants =
    [
        ("acme", "Acme Corporation"),
        ("globex", "Globex Industries"),
        ("initech", "Initech Solutions"),
    ];

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
        // 2FA showcase user — pre-enrolled with TwoFactorEnabledUserKey so the
        // /visuauth/two-factor/verify challenge flow is reachable without
        // running setup first. See SeedTwoFactorAsync below.
        (TwoFactorEnabledUserEmail, "twofactor.demo", "acme"),
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
        // MigrateAsync runs any pending migrations under
        // Data/Migrations/, creating the DB on first boot OR adding
        // new tables / columns when the schema evolves. Previous
        // EnsureCreatedAsync only created the DB if it didn't exist —
        // schema changes forced the owner to delete the .db file by
        // hand. Migrations make the dev loop self-healing.
        await db.Database.MigrateAsync(cancellationToken);

        // Seed tenants into the metadata table before users — the user rows
        // reference these tenant ids.
        foreach (var (id, displayName) in Tenants)
        {
            var exists = await db.VisuAuthTenants.AnyAsync(t => t.Id == id, cancellationToken);
            if (exists)
            {
                continue;
            }
            db.VisuAuthTenants.Add(new VisuAuthTenant
            {
                Id = id,
                DisplayName = displayName,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

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

        await SeedTwoFactorAsync(userManager);
        await SeedExternalProviderConfigsAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>
    /// Idempotently inserts a row in <c>VisuAuthExternalProviderConfigs</c>
    /// for every scheme the host registered through
    /// <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>. The
    /// seeded row pulls ClientId from appsettings / user-secrets when
    /// present so a working appsettings setup keeps rendering its button
    /// immediately after the admin store goes live. EncryptedClientSecret
    /// stays null on first seed — the admin must re-enter the secret via
    /// the UI to enable the dynamic overlay (or keep using the
    /// appsettings-bound static options, which still override when the row
    /// is missing).
    /// </summary>
    /// <remarks>
    /// Reads the registry instead of hardcoding the list — adding a new
    /// provider in Program.cs (one <c>RegisterScheme</c> call) automatically
    /// surfaces a seeded row here without a second edit.
    /// </remarks>
    private static async Task SeedExternalProviderConfigsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var store = services.GetService<IExternalProviderConfigStore>();
        var registry = services.GetService<IExternalProviderRegistry>();
        if (store is null || registry is null)
        {
            // Consumer never wired AddVisuAuthExternalProviderConfigStore /
            // AddVisuAuthDynamicExternalProviderOptions — nothing to seed.
            // (Tests use this path to skip the EF table.)
            return;
        }
        var configuration = services.GetRequiredService<IConfiguration>();
        var section = configuration.GetSection("ExternalProviders");

        foreach (var reg in registry.Registrations)
        {
            var defaultClientId = section[$"{reg.Scheme}:ClientId"];
            var defaultSecret = section[$"{reg.Scheme}:ClientSecret"];
            // Enable the row out of the gate when BOTH ClientId and Secret
            // are already in configuration — the appsettings-bound static
            // options will carry the secret until the admin replaces them
            // via the UI.
            var defaultEnabled = !string.IsNullOrWhiteSpace(defaultClientId)
                                 && !string.IsNullOrWhiteSpace(defaultSecret);
            await store.EnsureSchemeAsync(
                reg.Scheme,
                reg.Scheme,
                tenantId: null,
                defaultClientId: defaultClientId,
                defaultIsEnabled: defaultEnabled,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Enrols <see cref="TwoFactorEnabledUserEmail"/> with a deterministic
    /// authenticator key so the TOTP challenge flow is reachable from
    /// <c>/visuauth/login</c> without manual setup. Idempotent.
    /// </summary>
    private static async Task SeedTwoFactorAsync(UserManager<ApplicationUser> userManager)
    {
        var demo = await userManager.FindByEmailAsync(TwoFactorEnabledUserEmail);
        if (demo is null)
        {
            return;
        }

        if (await userManager.GetTwoFactorEnabledAsync(demo))
        {
            return;
        }

        // SetAuthenticationTokenAsync writes into AspNetUserTokens with the
        // exact key (`AuthenticatorKey`) that UserManager.GetAuthenticatorKeyAsync
        // reads back, so the seeded value is the one TOTP validation uses.
        var setKey = await userManager.SetAuthenticationTokenAsync(
            demo,
            "[AspNetUserStore]",
            "AuthenticatorKey",
            TwoFactorEnabledUserKey);
        if (!setKey.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed authenticator key for '{TwoFactorEnabledUserEmail}': {string.Join("; ", setKey.Errors.Select(e => e.Description))}");
        }

        var enable = await userManager.SetTwoFactorEnabledAsync(demo, true);
        if (!enable.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to enable 2FA on seeded user '{TwoFactorEnabledUserEmail}': {string.Join("; ", enable.Errors.Select(e => e.Description))}");
        }

        // Seed a deterministic recovery batch directly into the AspNetUserTokens
        // row Identity reads back via RedeemTwoFactorRecoveryCodeAsync. Codes
        // are stored as semicolon-joined plaintext under the canonical
        // ([AspNetUserStore], RecoveryCodes) key — same shape the runtime path
        // (UserManager.GenerateNewTwoFactorRecoveryCodesAsync → ReplaceCodesAsync)
        // produces, just with stable values instead of random ones.
        var recoveryToken = string.Join(';', TwoFactorEnabledUserRecoveryCodes);
        var setRecovery = await userManager.SetAuthenticationTokenAsync(
            demo,
            "[AspNetUserStore]",
            "RecoveryCodes",
            recoveryToken);
        if (!setRecovery.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed recovery codes for '{TwoFactorEnabledUserEmail}': {string.Join("; ", setRecovery.Errors.Select(e => e.Description))}");
        }
    }
}
