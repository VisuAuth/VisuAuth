using System.Security.Claims;

namespace VisuAuth.EntraExternal.Web;

/// <summary>
/// Copies configured id_token claims onto the signed-in customer's
/// Microsoft Graph user profile. Invoked by
/// <see cref="EntraExternalLoginFlow"/> on a successful OIDC sign-in so
/// attributes a sign-up user flow collected (and emitted as claims) land
/// on the directory user without a Graph claims-mapping policy.
/// </summary>
/// <remarks>
/// Best-effort by contract: a sync failure must never block sign-in —
/// the implementation swallows Graph errors (logging them) and returns.
/// No-ops when profile sync is disabled in
/// <see cref="VisuAuth.EntraExternal.Web.Configuration.EntraExternalProfileSyncOptions.Enabled"/>.
/// </remarks>
public interface IEntraExternalProfileSync
{
    /// <summary>
    /// Maps the configured claims off <paramref name="principal"/> onto
    /// the Graph user identified by <paramref name="userObjectId"/> and
    /// persists them with a single PATCH. No-op when disabled or when no
    /// mapped claim is present on the token.
    /// </summary>
    Task SyncAsync(ClaimsPrincipal principal, string userObjectId, CancellationToken cancellationToken = default);
}
