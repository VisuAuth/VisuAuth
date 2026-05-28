using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.EntraExternal.Web.Configuration;
using VisuAuth.EntraExternal.Web.Internal;
using GraphUser = Microsoft.Graph.Models.User;

namespace VisuAuth.EntraExternal.Web;

/// <summary>
/// Default <see cref="IEntraExternalProfileSync"/>: reads the configured
/// claims off the OIDC principal and PATCHes them onto the Graph user
/// via the stable v1.0 <c>PATCH /users/{id}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The target Graph properties are restricted to a known set of standard
/// <see cref="GraphUser"/> profile fields (see <see cref="TryApply"/>);
/// unknown targets are skipped so a typo in configuration can't blow up
/// the PATCH. Custom directory extension properties are intentionally out
/// of scope — they need a registered schema extension and their
/// fully-qualified names, which is a separate (beta-adjacent) concern.
/// </para>
/// <para>
/// <b>Claim lookup is alias-aware.</b> Depending on whether the OIDC
/// handler maps inbound claims, the standard name claims arrive either as
/// short OIDC names (<c>given_name</c>) or the legacy SOAP URIs
/// (<c>http://schemas.xmlsoap.org/.../givenname</c>). The lookup checks
/// the configured claim type first, then a small alias table for the two
/// built-in defaults, so the out-of-the-box mapping works under either
/// setting.
/// </para>
/// </remarks>
public sealed class EntraExternalProfileSync(
    GraphServiceClient graphClient,
    IOptions<EntraExternalWebOptions> options,
    ILogger<EntraExternalProfileSync> logger) : IEntraExternalProfileSync
{
    private readonly GraphServiceClient _graph =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    private readonly EntraExternalWebOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<EntraExternalProfileSync> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    // Alias table for the two built-in name claims — covers the case where
    // the OIDC handler mapped inbound claims to the legacy SOAP URIs.
    private static readonly Dictionary<string, string> ClaimAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["given_name"] = ClaimTypes.GivenName,
            ["family_name"] = ClaimTypes.Surname,
        };

    /// <inheritdoc />
    public async Task SyncAsync(ClaimsPrincipal principal, string userObjectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!_options.ProfileSync.Enabled || string.IsNullOrEmpty(userObjectId))
        {
            return;
        }

        var patch = new GraphUser();
        var applied = 0;
        foreach (var (claimType, graphProperty) in _options.ProfileSync.ClaimToGraphProperty)
        {
            if (string.IsNullOrWhiteSpace(graphProperty))
            {
                continue; // consumer mapped a default to "" to drop it
            }
            var value = ResolveClaim(principal, claimType);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (TryApply(patch, graphProperty, value))
            {
                applied++;
            }
            else
            {
                _logger.ProfileSyncUnknownProperty(graphProperty);
            }
        }

        if (applied == 0)
        {
            return; // nothing on the token to write — skip the round-trip
        }

        try
        {
            await _graph.Users[userObjectId].PatchAsync(patch, cancellationToken: cancellationToken);
        }
        catch (ODataError ex)
        {
            // Best-effort: a failed profile sync must not break sign-in.
            // The user is already authenticated; surface the Graph reason
            // in the log for the operator and carry on.
            _logger.ProfileSyncFailed(ex, userObjectId, ex.Error?.Message);
        }
    }

    private static string? ResolveClaim(ClaimsPrincipal principal, string claimType)
    {
        var direct = principal.FindFirst(claimType)?.Value;
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }
        return ClaimAliases.TryGetValue(claimType, out var alias)
            ? principal.FindFirst(alias)?.Value
            : null;
    }

    /// <summary>
    /// Sets the typed Graph <see cref="GraphUser"/> property named by
    /// <paramref name="graphProperty"/> (case-insensitive) to
    /// <paramref name="value"/>. Returns false for unsupported targets so
    /// the caller can log and skip. Reflection-free by design — the
    /// supported surface is an explicit allow-list.
    /// </summary>
    private static bool TryApply(GraphUser patch, string graphProperty, string value)
    {
        switch (graphProperty.Trim().ToLowerInvariant())
        {
            case "givenname": patch.GivenName = value; return true;
            case "surname": patch.Surname = value; return true;
            case "displayname": patch.DisplayName = value; return true;
            case "jobtitle": patch.JobTitle = value; return true;
            case "department": patch.Department = value; return true;
            case "companyname": patch.CompanyName = value; return true;
            case "city": patch.City = value; return true;
            case "state": patch.State = value; return true;
            case "country": patch.Country = value; return true;
            case "postalcode": patch.PostalCode = value; return true;
            case "streetaddress": patch.StreetAddress = value; return true;
            default: return false;
        }
    }
}
