namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Stable storage keys for the Entra adapter's DB-backed configuration. Shared
/// by the overlay configurator, the static snapshot, and the admin config
/// schema so the three never drift.
/// </summary>
public static class EntraAdapterConfigKeys
{
    /// <summary>Adapter key the <c>IAdapterConfigStore</c> rows are filed under.</summary>
    public const string Adapter = "Entra";

    public const string TenantId = "TenantId";
    public const string ClientId = "ClientId";
    public const string ClientSecret = "ClientSecret";
    public const string AppRoleResourceId = "AppRoleResourceId";
    public const string GraphBaseUrl = "GraphBaseUrl";
    public const string DefaultEmailDomain = "DefaultEmailDomain";
}
