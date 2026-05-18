namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Source of truth for which external-provider schemes the host has actually
/// wired through <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>.
/// The admin page consults this instead of guessing from the DB so the list
/// reflects what's truly runnable: a scheme without a registered handler
/// can't sign anyone in, and a scheme with no row in the DB can still be
/// surfaced for first-time configuration.
/// </summary>
/// <remarks>
/// Populated at startup as a singleton; immutable afterwards. Add a new
/// provider at runtime requires registering it through the standard DI
/// helper and restarting — the runtime
/// <c>IAuthenticationSchemeProvider.AddScheme()</c> path lives in a
/// follow-up PR that adds generic OAuth / OIDC support via the admin UI.
/// </remarks>
public interface IExternalProviderRegistry
{
    /// <summary>
    /// Every scheme the host wired through
    /// <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>, in
    /// the order the calls were made. Empty when the consumer is using
    /// static <c>AddXxx(o =&gt; ...)</c> options exclusively.
    /// </summary>
    IReadOnlyList<ExternalProviderSchemeRegistration> Registrations { get; }

    /// <summary>True when the host wired a dynamic-options configurator for <paramref name="scheme"/>.</summary>
    bool IsRegistered(string scheme);
}

/// <summary>
/// One scheme that the host wired through
/// <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>. The
/// options type is needed at runtime so the cache invalidator can resolve
/// the matching <c>IOptionsMonitorCache&lt;TOptions&gt;</c> via reflection.
/// </summary>
public sealed record ExternalProviderSchemeRegistration(string Scheme, Type OptionsType);
