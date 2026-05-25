namespace VisuAuth.EndUserUi.Authentication;

/// <summary>
/// Identifies which entry point produced a sign-in attempt. Lives on every
/// audit payload via the <c>channel</c> key so the admin log can filter
/// "all failed API logins from the last hour" without ad-hoc joins.
/// </summary>
/// <remarks>
/// Add a new entry when a new sign-in surface lands (mobile native flow,
/// SAML bridge, etc) — the audit payload key is the lowercased enum name
/// so consumers can predict the value without checking the source.
/// </remarks>
public enum SignInChannel
{
    /// <summary>Browser-rendered Razor login page (<c>/visuauth/login</c>).</summary>
    Web,

    /// <summary>Minimal-API JSON endpoint (<c>/visuauth/api/auth/login</c>) — mobile / native client.</summary>
    Api,
}
