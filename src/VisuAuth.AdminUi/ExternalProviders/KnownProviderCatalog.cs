namespace VisuAuth.AdminUi.ExternalProviders;

/// <summary>
/// Built-in catalogue of ~20 commonly-used OAuth providers that the admin
/// page surfaces as discoverable cards even when the consumer hasn't wired
/// them yet. Every entry derives from <c>OAuthOptions</c>, which is the
/// constraint our dynamic options overlay accepts — OIDC providers
/// (Microsoft Entra, Auth0, Okta, Keycloak) are intentionally excluded from
/// this list because the runtime overlay for them ships in a follow-up PR.
/// </summary>
/// <remarks>
/// Maintenance rule: keep the package id + extension method names in sync
/// with the upstream NuGet packages. When an upstream API moves (renames,
/// changes the options type), the admin's "How to activate" snippet here is
/// what we'd have to update; the runtime side is unaffected because the
/// consumer's <c>AddXxx</c> + <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>
/// always uses the upstream types directly.
/// </remarks>
public static class KnownProviderCatalog
{
    /// <summary>
    /// Ordered list shown in the admin UI. Ordering favours discoverability
    /// (Microsoft / Google first, niche providers last) over alphabetical.
    /// </summary>
    public static readonly IReadOnlyList<KnownExternalProvider> All =
    [
        // --- Major ---
        new("Microsoft", "Microsoft", KnownProviderCategory.Major,
            "Microsoft.AspNetCore.Authentication.MicrosoftAccount",
            "MicrosoftAccountOptions", "AddMicrosoftAccount",
            "https://learn.microsoft.com/aspnet/core/security/authentication/social/microsoft-logins"),
        new("Google", "Google", KnownProviderCategory.Major,
            "Microsoft.AspNetCore.Authentication.Google",
            "GoogleOptions", "AddGoogle",
            "https://learn.microsoft.com/aspnet/core/security/authentication/social/google-logins"),
        new("Apple", "Apple", KnownProviderCategory.Major,
            "AspNet.Security.OAuth.Apple",
            "AppleAuthenticationOptions", "AddApple",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Apple"),
        new("Facebook", "Facebook", KnownProviderCategory.Major,
            "Microsoft.AspNetCore.Authentication.Facebook",
            "FacebookOptions", "AddFacebook",
            "https://learn.microsoft.com/aspnet/core/security/authentication/social/facebook-logins"),

        // --- Developer ---
        new("GitHub", "GitHub", KnownProviderCategory.Developer,
            "AspNet.Security.OAuth.GitHub",
            "GitHubAuthenticationOptions", "AddGitHub",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.GitHub"),
        new("GitLab", "GitLab", KnownProviderCategory.Developer,
            "AspNet.Security.OAuth.GitLab",
            "GitLabAuthenticationOptions", "AddGitLab",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.GitLab"),
        new("Reddit", "Reddit", KnownProviderCategory.Developer,
            "AspNet.Security.OAuth.Reddit",
            "RedditAuthenticationOptions", "AddReddit",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Reddit"),

        // --- Social ---
        new("LinkedIn", "LinkedIn", KnownProviderCategory.Social,
            "AspNet.Security.OAuth.LinkedIn",
            "LinkedInAuthenticationOptions", "AddLinkedIn",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.LinkedIn"),
        new("X", "X (Twitter)", KnownProviderCategory.Social,
            "AspNet.Security.OAuth.Twitter",
            "TwitterAuthenticationOptions", "AddTwitter",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Twitter"),
        new("Discord", "Discord", KnownProviderCategory.Social,
            "AspNet.Security.OAuth.Discord",
            "DiscordAuthenticationOptions", "AddDiscord",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Discord"),
        new("Slack", "Slack", KnownProviderCategory.Social,
            "AspNet.Security.OAuth.Slack",
            "SlackAuthenticationOptions", "AddSlack",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Slack"),
        new("Twitch", "Twitch", KnownProviderCategory.Social,
            "AspNet.Security.OAuth.Twitch",
            "TwitchAuthenticationOptions", "AddTwitch",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Twitch"),
        new("Spotify", "Spotify", KnownProviderCategory.Social,
            "AspNet.Security.OAuth.Spotify",
            "SpotifyAuthenticationOptions", "AddSpotify",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Spotify"),

        // --- Business ---
        new("Amazon", "Amazon", KnownProviderCategory.Business,
            "AspNet.Security.OAuth.Amazon",
            "AmazonAuthenticationOptions", "AddAmazon",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Amazon"),
        new("Salesforce", "Salesforce", KnownProviderCategory.Business,
            "AspNet.Security.OAuth.Salesforce",
            "SalesforceAuthenticationOptions", "AddSalesforce",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Salesforce"),
        new("Notion", "Notion", KnownProviderCategory.Business,
            "AspNet.Security.OAuth.Notion",
            "NotionAuthenticationOptions", "AddNotion",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Notion"),
        new("PayPal", "PayPal", KnownProviderCategory.Business,
            "AspNet.Security.OAuth.Paypal",
            "PaypalAuthenticationOptions", "AddPaypal",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Paypal"),
        new("Patreon", "Patreon", KnownProviderCategory.Business,
            "AspNet.Security.OAuth.Patreon",
            "PatreonAuthenticationOptions", "AddPatreon",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Patreon"),
        new("Zoom", "Zoom", KnownProviderCategory.Business,
            "AspNet.Security.OAuth.Zoom",
            "ZoomAuthenticationOptions", "AddZoom",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Zoom"),
        new("Shopify", "Shopify", KnownProviderCategory.Business,
            "AspNet.Security.OAuth.Shopify",
            "ShopifyAuthenticationOptions", "AddShopify",
            "https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Shopify"),
    ];

    private static readonly Dictionary<string, KnownExternalProvider> BySchema =
        All.ToDictionary(p => p.Scheme, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the catalogue entry for <paramref name="scheme"/>, or null when unknown.</summary>
    public static KnownExternalProvider? Find(string scheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        return BySchema.TryGetValue(scheme, out var hit) ? hit : null;
    }
}
