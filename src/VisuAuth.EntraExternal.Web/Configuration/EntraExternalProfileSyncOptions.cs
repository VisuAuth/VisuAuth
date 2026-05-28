namespace VisuAuth.EntraExternal.Web.Configuration;

/// <summary>
/// Controls the optional "copy id_token claims onto the Graph user
/// profile on sign-in" behaviour. When an Entra External sign-up user
/// flow collects attributes (given name, surname, country, custom
/// fields) and emits them as token claims, this maps those claims onto
/// the directory user's standard Graph properties so the admin UI and
/// downstream queries see them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Microsoft's hosted sign-up user flow writes
/// some attributes to the directory itself, but custom / progressive
/// attributes frequently live only on the token unless an admin wires a
/// claims-mapping policy. Rather than require that policy plumbing,
/// VisuAuth can read the claims it already receives on the OIDC callback
/// and PATCH them onto the user via the standard v1.0 Graph
/// <c>PATCH /users/{id}</c> — no beta user-flow API needed.
/// </para>
/// <para>
/// <b>Scope (v0.3).</b> This is the attribute-mapping half of the
/// originally-planned signup customization. Listing / editing the user
/// flows themselves stays in the Entra portal because that surface is
/// beta-only in Microsoft Graph and we keep the adapter on the stable
/// v1.0 SDK.
/// </para>
/// </remarks>
public sealed class EntraExternalProfileSyncOptions
{
    /// <summary>
    /// Master switch. Defaults to <c>false</c> — the adapter never writes
    /// to the directory on sign-in unless the consumer opts in. Off is the
    /// safe default: a misconfigured mapping can't silently overwrite
    /// directory data.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Map of <b>id_token claim type → Graph user property</b>. On each
    /// sign-in (when <see cref="Enabled"/>), every entry whose claim is
    /// present on the token sets the corresponding Graph property, then a
    /// single PATCH persists them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seeded with the two universal OIDC name claims
    /// (<c>given_name</c> → <c>givenName</c>, <c>family_name</c> →
    /// <c>surname</c>). Configuration binding ADDS to this dictionary
    /// rather than replacing it, so consumers list only their extra
    /// mappings (e.g. a custom attribute claim
    /// <c>extension_&lt;appId&gt;_country</c> → <c>country</c>) and keep
    /// the name defaults. To drop a default, map it to an empty string.
    /// </para>
    /// <para>
    /// The <b>values</b> (Graph properties) are limited to a supported
    /// set of standard <c>User</c> properties:
    /// <c>givenName</c>, <c>surname</c>, <c>displayName</c>,
    /// <c>jobTitle</c>, <c>department</c>, <c>companyName</c>,
    /// <c>city</c>, <c>state</c>, <c>country</c>, <c>postalCode</c>,
    /// <c>streetAddress</c> (case-insensitive). Unknown targets are
    /// ignored (logged once) — VisuAuth deliberately doesn't write Graph
    /// extension properties from this path, which would need their
    /// schema-qualified names and the directory schema extension to be
    /// registered first.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> ClaimToGraphProperty { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["given_name"] = "givenName",
            ["family_name"] = "surname",
        };
}
