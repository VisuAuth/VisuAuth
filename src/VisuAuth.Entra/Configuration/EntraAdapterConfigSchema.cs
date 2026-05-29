using VisuAuth.Abstractions.Configuration;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Describes the Entra adapter's editable settings for the admin config page.
/// "From code" presence is read from <see cref="EntraConfigStaticSnapshot"/>
/// (captured when options first materialize), so the page can badge each field
/// as coming from the DB, from code, or both.
/// </summary>
public sealed class EntraAdapterConfigSchema(EntraConfigStaticSnapshot snapshot) : IAdapterConfigSchema
{
    private readonly EntraConfigStaticSnapshot _snapshot =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public string Adapter => EntraAdapterConfigKeys.Adapter;

    public string DisplayName => "Microsoft Entra ID";

    public IReadOnlyList<AdapterConfigField> Fields { get; } =
    [
        new()
        {
            Key = EntraAdapterConfigKeys.TenantId,
            Label = "Tenant ID",
            IsRequired = true,
            HelpText = "Directory (tenant) GUID of the Entra tenant VisuAuth manages.",
        },
        new()
        {
            Key = EntraAdapterConfigKeys.ClientId,
            Label = "Client ID",
            IsRequired = true,
            HelpText = "Application (client) GUID of the registered app.",
        },
        new()
        {
            Key = EntraAdapterConfigKeys.ClientSecret,
            Label = "Client secret",
            IsSecret = true,
            IsRequired = true,
            HelpText = "Stored encrypted at rest. Leave blank to keep the current value.",
        },
        new()
        {
            Key = EntraAdapterConfigKeys.AppRoleResourceId,
            Label = "App role resource ID",
            HelpText = "App (client) ID whose appRoles back the roles page. Defaults to Client ID.",
        },
        new()
        {
            Key = EntraAdapterConfigKeys.GraphBaseUrl,
            Label = "Graph base URL",
            HelpText = "Override for sovereign clouds. Defaults to the public cloud endpoint.",
        },
        new()
        {
            Key = EntraAdapterConfigKeys.DefaultEmailDomain,
            Label = "Default email domain",
            HelpText = "Verified domain suggested on the create-user form.",
        },
    ];

    public bool HasCodeValue(string key) => _snapshot.HasValue(key);

    public string? GetCodeValue(string key) => _snapshot.GetValue(key);
}
