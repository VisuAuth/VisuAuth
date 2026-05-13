using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace VisuAuth.Identity.Users;

/// <summary>
/// Generates a cryptographically random temporary password that satisfies the
/// configured <see cref="PasswordOptions"/>. The result is meant to be handed
/// to the user once — VisuAuth never persists the plaintext.
/// </summary>
internal static class TemporaryPasswordGenerator
{
    // Visually unambiguous character pools: I/O, l/o, 0/1 are intentionally
    // excluded so admins can dictate the password over the phone without
    // ambiguity.
    private const string Uppers = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowers = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*";

    public static string Generate(PasswordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pool = new System.Text.StringBuilder();
        var required = new List<char>();

        AppendPool(pool, Uppers);
        AppendPool(pool, Lowers);
        AppendPool(pool, Digits);

        if (options.RequireUppercase)
        {
            required.Add(PickRandom(Uppers));
        }
        if (options.RequireLowercase)
        {
            required.Add(PickRandom(Lowers));
        }
        if (options.RequireDigit)
        {
            required.Add(PickRandom(Digits));
        }
        if (options.RequireNonAlphanumeric)
        {
            required.Add(PickRandom(Symbols));
            AppendPool(pool, Symbols);
        }

        // 16 is a sensible floor regardless of policy: long enough to resist
        // brute force, short enough to read out loud.
        var targetLength = Math.Max(options.RequiredLength, 16);

        var poolChars = pool.ToString();
        while (required.Count < targetLength)
        {
            required.Add(PickRandom(poolChars));
        }

        // Fisher-Yates with a CSPRNG so the leading required characters are not
        // always in the same slots.
        var buffer = required.ToArray();
        for (var i = buffer.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        return new string(buffer);
    }

    private static char PickRandom(string source)
        => source[RandomNumberGenerator.GetInt32(source.Length)];

    private static void AppendPool(System.Text.StringBuilder pool, string source)
        => pool.Append(source);
}
