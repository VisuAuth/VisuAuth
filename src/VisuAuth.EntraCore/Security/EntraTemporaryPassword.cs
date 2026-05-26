using System.Security.Cryptography;

namespace VisuAuth.EntraCore.Security;

/// <summary>
/// Generates a one-time temporary password the Entra admin hands to the
/// user after Create / ResetPassword. Independent of
/// <c>TemporaryPasswordGenerator</c> in <c>VisuAuth.Identity</c> so the
/// Entra adapter family doesn't acquire a dependency on the Identity
/// package (CLAUDE.md §2.5 — adapters stay independent).
/// </summary>
/// <remarks>
/// <para>
/// Output shape: 12 characters drawn from a mixed alphabet (upper + lower +
/// digit + symbol), with at least one of each character class so the result
/// always satisfies the default Entra password policy (8+ chars, 3 of 4
/// classes). Visually ambiguous characters (<c>0OIl1</c>) are excluded so
/// the admin can read it out loud over the phone without round-trips.
/// </para>
/// <para>
/// Uses <see cref="RandomNumberGenerator"/> end-to-end. <c>Random</c> would
/// be wrong here — even though the password is one-time and force-rotated,
/// the operator is allowed to assume the value isn't predictable.
/// </para>
/// </remarks>
public static class EntraTemporaryPassword
{
    private const string Upper = "ABCDEFGHJKMNPQRSTUVWXYZ";   // sans I, L, O
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";   // sans l, o
    private const string Digit = "23456789";                    // sans 0, 1
    private const string Symbol = "!@#$%^&*-_=+";
    private const int Length = 12;

    public static string Generate()
    {
        var all = Upper + Lower + Digit + Symbol;
        Span<char> buf = stackalloc char[Length];
        buf[0] = Pick(Upper);
        buf[1] = Pick(Lower);
        buf[2] = Pick(Digit);
        buf[3] = Pick(Symbol);
        for (var i = 4; i < Length; i++)
        {
            buf[i] = Pick(all);
        }
        Shuffle(buf);
        return new string(buf);
    }

    private static char Pick(string alphabet)
        => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(Span<char> buffer)
    {
        for (var i = buffer.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
    }
}
