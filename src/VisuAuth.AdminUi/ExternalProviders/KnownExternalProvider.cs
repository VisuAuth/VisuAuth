namespace VisuAuth.AdminUi.ExternalProviders;

/// <summary>
/// One row in the built-in catalogue of OAuth providers VisuAuth's admin UI
/// recognises. Each entry is a hint about what the consumer can wire — it
/// does NOT register a handler in DI, just feeds the admin's "Available
/// providers" section so unfamiliar admins discover what's possible and get
/// a copy-pasteable wiring snippet for <c>Program.cs</c>.
/// </summary>
/// <param name="Scheme">Canonical scheme name. The consumer's
/// <c>.AddXxx("Microsoft", ...)</c> must use this exact value for the
/// catalogue entry to merge with the runtime registration into a single
/// "active" card.</param>
/// <param name="DisplayName">Human-readable label shown on the card.</param>
/// <param name="Category">Loose grouping used by the admin page to lay out
/// providers under bucketed headings.</param>
/// <param name="NuGetPackageId">Package the consumer must install. Shown
/// verbatim in the install snippet — copy/paste into <c>dotnet add</c>.</param>
/// <param name="OptionsTypeName">Friendly options-type name (e.g.
/// <c>GoogleOptions</c>) shown in the wiring snippet. Not used for runtime
/// reflection — the actual <c>typeof()</c> comes from the consumer's call to
/// <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>.</param>
/// <param name="AddExtensionMethod">Name of the fluent extension that
/// registers the handler (e.g. <c>AddGoogle</c>). Used in the snippet.</param>
/// <param name="DocsUrl">External docs link for setting up the provider's
/// developer app. Optional — when null, the card omits the docs button.</param>
public sealed record KnownExternalProvider(
    string Scheme,
    string DisplayName,
    KnownProviderCategory Category,
    string NuGetPackageId,
    string OptionsTypeName,
    string AddExtensionMethod,
    string? DocsUrl = null);

/// <summary>
/// Bucket used by the admin page to group "Available providers" cards under
/// headings. Categorisation is loose — pick the closest fit for new entries.
/// </summary>
public enum KnownProviderCategory
{
    /// <summary>Microsoft, Google, Apple, Facebook — the providers most end users have an account with.</summary>
    Major,

    /// <summary>GitHub, GitLab, Reddit — developer-leaning communities.</summary>
    Developer,

    /// <summary>LinkedIn, X, Discord, Slack, Twitch, Spotify — social / media platforms.</summary>
    Social,

    /// <summary>Amazon, Salesforce, Notion, PayPal, Patreon, Zoom, Shopify — business / productivity SaaS.</summary>
    Business,
}
