using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sample.WebApp.Data;

namespace VisuAuth.IntegrationTests.EndUser;

/// <summary>
/// Shared scaffolding for the two-factor integration tests: creates ad-hoc
/// users to keep each test fully isolated from the seeded fixtures, computes
/// live TOTP codes against the real authenticator key, and exposes a
/// per-test antiforgery helper.
/// </summary>
internal static class TwoFactorTestHelpers
{
    /// <summary>The seeded password every helper-created user gets.</summary>
    public const string DefaultPassword = "Pa$$w0rd!";

    /// <summary>
    /// Provisions a fresh user with a deterministic email derived from
    /// <paramref name="prefix"/> + a guid suffix. The user starts with 2FA
    /// disabled and an empty authenticator key — enable per-test as needed.
    /// </summary>
    public static async Task<ApplicationUser> CreateAdHocUserAsync(
        VisuAuthTestFactory factory,
        string prefix,
        string tenantId = "acme")
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"{prefix}.{suffix}@example.com";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
        };
        var create = await userManager.CreateAsync(user, DefaultPassword);
        create.Succeeded.Should().BeTrue("the helper must be able to create test users");
        return user;
    }

    /// <summary>
    /// Enrols <paramref name="user"/> in TOTP 2FA against a fresh authenticator
    /// key and returns the canonical Base32 key for use in subsequent code
    /// generation calls.
    /// </summary>
    public static async Task<string> EnableTotpAsync(VisuAuthTestFactory factory, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(user);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var managed = await userManager.FindByIdAsync(user.Id);
        managed.Should().NotBeNull();

        var reset = await userManager.ResetAuthenticatorKeyAsync(managed!);
        reset.Succeeded.Should().BeTrue();

        var key = await userManager.GetAuthenticatorKeyAsync(managed!);
        key.Should().NotBeNullOrEmpty();

        var enable = await userManager.SetTwoFactorEnabledAsync(managed!, true);
        enable.Succeeded.Should().BeTrue();
        return key!;
    }

    /// <summary>
    /// Computes the TOTP code an authenticator app would currently show for
    /// <paramref name="user"/>. Equivalent to the call <see cref="UserManager{TUser}"/>
    /// makes internally to verify a submitted code, so the result is always
    /// the value the live verifier expects in this exact instant.
    /// </summary>
    /// <summary>
    /// Computes the TOTP code an authenticator app would currently show for
    /// <paramref name="user"/>'s seeded shared key. ASP.NET Identity's built-in
    /// <c>AuthenticatorTokenProvider.GenerateAsync</c> always returns
    /// <c>string.Empty</c> by design (the framework only validates incoming
    /// codes — generation is the device's job), so the helper inlines RFC 6238
    /// instead.
    /// </summary>
    public static async Task<string> GetCurrentTotpCodeAsync(VisuAuthTestFactory factory, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(user);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var managed = await userManager.FindByIdAsync(user.Id);
        managed.Should().NotBeNull();
        var key = await userManager.GetAuthenticatorKeyAsync(managed!);
        key.Should().NotBeNullOrEmpty($"a shared key must be enrolled for {user.Email} before computing TOTP");
        return ComputeTotp(key!);
    }

    /// <summary>
    /// Inline RFC 6238 6-digit TOTP generator (HMACSHA1, 30-second window).
    /// Matches what an authenticator app produces and what
    /// <see cref="SignInManager{TUser}.TwoFactorAuthenticatorSignInAsync"/>
    /// validates against. HMACSHA1 is mandated by the RFC; the analyzer
    /// warning is suppressed because TOTP interoperability requires it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 mandates HMACSHA1 for authenticator interop.")]
    public static string ComputeTotp(string base32Key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base32Key);

        var keyBytes = Base32Decode(base32Key);
        var unixTimestamp = (long)Math.Round((DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds);
        var timestep = unixTimestamp / 30L;

        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(timestep & 0xFF);
            timestep >>= 8;
        }

        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        var code = binary % 1_000_000;
        return code.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sanitized = input.Replace(" ", string.Empty, StringComparison.Ordinal)
                             .TrimEnd('=')
                             .ToUpperInvariant();

        var bytes = new List<byte>(sanitized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var ch in sanitized)
        {
            var idx = alphabet.IndexOf(ch, StringComparison.Ordinal);
            if (idx < 0)
            {
                throw new FormatException($"Invalid Base32 character: {ch}");
            }
            buffer = (buffer << 5) | idx;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }
        return bytes.ToArray();
    }

    /// <summary>
    /// Walks <paramref name="client"/> through the full sign-in pipeline for a
    /// 2FA-enabled user — password POST, follow the 302 to the challenge,
    /// compute and submit the live TOTP code. Leaves the cookie container
    /// holding the FULL Identity cookie so subsequent requests pass
    /// <c>[Authorize]</c> checks.
    /// </summary>
    public static async Task SignInThroughTwoFactorAsync(
        VisuAuthTestFactory factory,
        HttpClient client,
        ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(user);

        var loginToken = await GetTokenAsync(client, "/visuauth/login");
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken,
            ["Form.Email"] = user.Email!,
            ["Form.Password"] = DefaultPassword,
        });
        var loginResponse = await client.PostAsync(new Uri("/visuauth/login", UriKind.Relative), loginForm);
        // The Login post lands as a 302 to /visuauth/two-factor/verify when
        // 2FA is on. With AllowAutoRedirect the test client may have
        // followed it already; either way the partial cookie is now set.
        loginResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK);

        var verifyToken = await GetTokenAsync(client, "/visuauth/two-factor/verify");
        var code = await GetCurrentTotpCodeAsync(factory, user);
        var verifyForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = verifyToken,
            ["Form.Code"] = code,
        });
        var verifyResponse = await client.PostAsync(
            new Uri("/visuauth/two-factor/verify?handler=Authenticator", UriKind.Relative),
            verifyForm);
        verifyResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK);
    }

    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative));
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} must succeed (status {response.StatusCode})");
        var body = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(body);
        match.Success.Should().BeTrue($"{url} must render at least one antiforgery-protected form");
        return match.Groups[1].Value;
    }
}
