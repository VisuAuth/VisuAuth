namespace VisuAuth.Abstractions.Configuration;

/// <summary>
/// Describes the editable settings of a backend adapter so the admin config
/// page can render an editor generically — without the AdminUi package taking
/// a dependency on any specific adapter. An adapter (e.g. the Entra adapter)
/// registers one implementation; the page resolves
/// <see cref="IEnumerable{T}"/> of these and renders a section per adapter.
/// </summary>
public interface IAdapterConfigSchema
{
    /// <summary>
    /// Stable adapter key the matching <see cref="IAdapterConfigStore"/> rows
    /// are filed under (e.g. <c>"Entra"</c>).
    /// </summary>
    string Adapter { get; }

    /// <summary>Human-readable adapter name shown as the section heading.</summary>
    string DisplayName { get; }

    /// <summary>The settings the operator can edit, in display order.</summary>
    IReadOnlyList<AdapterConfigField> Fields { get; }

    /// <summary>
    /// True when the adapter currently has a non-DB (code / appsettings /
    /// user-secrets) value for <paramref name="key"/>. Drives the "From code"
    /// source badge alongside the store's "From DB" badge.
    /// </summary>
    bool HasCodeValue(string key);

    /// <summary>
    /// The code-supplied value to display for a non-secret <paramref name="key"/>,
    /// or <see langword="null"/> for a secret key (never surfaced) or when no
    /// code value exists.
    /// </summary>
    string? GetCodeValue(string key);
}

/// <summary>One editable setting in an <see cref="IAdapterConfigSchema"/>.</summary>
public sealed record AdapterConfigField
{
    /// <summary>Storage key (matches <see cref="IAdapterConfigStore"/> rows).</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable label shown next to the input.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// When true the value is rendered as a write-only password field and is
    /// stored encrypted; its plaintext is never returned to the browser.
    /// </summary>
    public bool IsSecret { get; init; }

    /// <summary>Whether the adapter needs this setting to function.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Optional hint shown under the input.</summary>
    public string? HelpText { get; init; }
}
