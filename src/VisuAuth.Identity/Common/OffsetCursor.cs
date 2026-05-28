using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace VisuAuth.Identity.Common;

/// <summary>
/// Encodes a row offset as the opaque forward cursor that EF-backed stores
/// hand back through <see cref="Abstractions.Common.PagedResult{T}.NextCursor"/>.
/// </summary>
/// <remarks>
/// EF stores have random access, so the simplest cursor that fits the
/// forward-only contract is the offset of the next page wrapped in a base64url
/// token — opaque to the caller (who must not parse it) yet trivially decodable
/// here. A malformed, tampered, or null cursor decodes to offset 0 (the first
/// page) rather than throwing, so a hand-edited query string can never 500 the
/// list endpoint.
/// </remarks>
internal static class OffsetCursor
{
    // A one-char tag in front of the number keeps a decoded payload from
    // looking like an arbitrary integer and makes the format explicit.
    private const char Tag = 'o';

    public static string Encode(int offset)
    {
        var payload = Tag + offset.ToString(CultureInfo.InvariantCulture);
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    public static int Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            if (raw.Length > 1 && raw[0] == Tag &&
                int.TryParse(raw.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var offset) &&
                offset >= 0)
            {
                return offset;
            }
        }
        catch (FormatException)
        {
            // Not valid base64url — treat as "first page" below.
        }

        return 0;
    }
}
