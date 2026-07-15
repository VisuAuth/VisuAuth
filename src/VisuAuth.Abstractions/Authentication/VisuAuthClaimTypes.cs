namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Claim types VisuAuth mints and reads. Backend-agnostic: an adapter that
/// issues its own tokens is expected to use these names so the rest of the
/// stack keeps working.
/// </summary>
public static class VisuAuthClaimTypes
{
    /// <summary>
    /// The user's security stamp at the moment the token was issued. Compared
    /// against the user's current stamp on every authenticated request and on
    /// refresh, so rotating the stamp revokes outstanding tokens.
    /// </summary>
    public const string SecurityStamp = "visuauth_stamp";

    /// <summary>The tenant the token was issued for, when multi-tenancy is on.</summary>
    public const string TenantId = "tenant_id";
}
