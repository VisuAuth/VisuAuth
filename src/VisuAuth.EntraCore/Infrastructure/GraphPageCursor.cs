using System.Buffers.Text;
using System.Text;

namespace VisuAuth.EntraCore.Infrastructure;

/// <summary>
/// Translates between Microsoft Graph's <c>@odata.nextLink</c> continuation
/// URL and the opaque forward cursor that the Entra user stores expose through
/// <see cref="Abstractions.Common.PagedResult{T}.NextCursor"/>.
/// </summary>
/// <remarks>
/// <para>
/// Graph's only paging primitive is a forward continuation link carrying a
/// <c>$skiptoken</c>; following it means handing the raw URL back to
/// <c>GraphServiceClient.WithUrl(...)</c>. Round-tripping that URL through the
/// browser as a query-string cursor is convenient but dangerous: a tampered
/// cursor could redirect the next request — <b>with the app's bearer token
/// attached</b> — to an attacker-controlled host (a classic SSRF /
/// credential-leak vector).
/// </para>
/// <para>
/// <see cref="TryDecode"/> therefore refuses any decoded URL that isn't HTTPS
/// on the <i>same origin</i> (scheme + host + port) as the configured Graph
/// base URL. A cursor that fails validation is rejected (returns
/// <see langword="false"/>) so the caller falls back to the first page rather
/// than issuing a request to an untrusted endpoint.
/// </para>
/// </remarks>
public static class GraphPageCursor
{
    /// <summary>
    /// Encodes a Graph continuation link as an opaque, URL-safe cursor, or
    /// <see langword="null"/> when there is no next page.
    /// </summary>
    public static string? Encode(string? nextLink)
        => string.IsNullOrEmpty(nextLink)
            ? null
            : Base64Url.EncodeToString(Encoding.UTF8.GetBytes(nextLink));

    /// <summary>
    /// Decodes a cursor to a Graph continuation URL, but only when it resolves
    /// to an HTTPS URL on the same origin as <paramref name="graphBaseUrl"/>.
    /// Returns <see langword="false"/> for a null/empty/malformed/tampered or
    /// off-origin cursor without throwing.
    /// </summary>
    public static bool TryDecode(string? cursor, string graphBaseUrl, out string nextLink)
    {
        nextLink = string.Empty;

        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        if (!Uri.TryCreate(decoded, UriKind.Absolute, out var candidate) ||
            !Uri.TryCreate(graphBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        // Never send the bearer token over plaintext, and never to a different
        // host/port than the configured Graph endpoint.
        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !string.Equals(candidate.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            candidate.Port != baseUri.Port)
        {
            return false;
        }

        nextLink = candidate.AbsoluteUri;
        return true;
    }
}
