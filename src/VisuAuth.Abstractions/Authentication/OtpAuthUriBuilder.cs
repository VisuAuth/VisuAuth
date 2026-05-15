using System.Globalization;
using System.Text;

namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Pure helper that builds the <c>otpauth://totp/&lt;issuer&gt;:&lt;account&gt;?...</c>
/// URI consumed by Google Authenticator, Microsoft Authenticator, 1Password,
/// and friends. Lives in <c>Abstractions</c> so adapters and pages share one
/// implementation — keeping the format spec (RFC 6238 / Key Uri Format) in
/// one place.
/// </summary>
public static class OtpAuthUriBuilder
{
    private const string Algorithm = "SHA1";
    private const int Digits = 6;
    private const int PeriodSeconds = 30;

    /// <summary>
    /// Builds the <c>otpauth://</c> URI. <paramref name="issuer"/> and
    /// <paramref name="accountName"/> must be non-empty; <paramref name="secretBase32"/>
    /// is the shared key as Base32 (no padding, no whitespace) — exactly the
    /// shape <c>UserManager.GetAuthenticatorKeyAsync</c> returns.
    /// </summary>
    public static string Build(string issuer, string accountName, string secretBase32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretBase32);

        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);
        var encodedSecret = Uri.EscapeDataString(secretBase32);

        return string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={encodedSecret}&issuer={encodedIssuer}&algorithm={Algorithm}&digits={Digits}&period={PeriodSeconds}");
    }

    /// <summary>
    /// Formats the raw shared key into 4-character groups separated by spaces
    /// for manual entry into authenticator apps that do not scan the QR.
    /// </summary>
    public static string FormatForManualEntry(string rawKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawKey);

        var sanitized = rawKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var sb = new StringBuilder(sanitized.Length + sanitized.Length / 4);
        for (var i = 0; i < sanitized.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                sb.Append(' ');
            }
            sb.Append(sanitized[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strips spaces, hyphens, and casing differences out of a TOTP / recovery
    /// code so the verifier sees the canonical form. Returns null when the
    /// input is null or whitespace.
    /// </summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }
        var sb = new StringBuilder(code.Length);
        foreach (var ch in code)
        {
            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
            {
                continue;
            }
            sb.Append(ch);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
