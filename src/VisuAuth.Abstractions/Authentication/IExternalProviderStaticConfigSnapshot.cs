namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Side-channel that captures the <c>ClientId</c> / <c>ClientSecret</c> a
/// scheme arrives at <em>before</em> VisuAuth's dynamic options overlay
/// applies the admin-edited values. Lets the admin UI distinguish two
/// sources of credentials and tell the operator who wins:
/// <list type="bullet">
///   <item><strong>Static</strong> — values from <c>appsettings</c>,
///     <c>user-secrets</c>, env vars, or an inline lambda in <c>Program.cs</c>
///     (anything that reached the options instance before our overlay ran).</item>
///   <item><strong>Database</strong> — values entered through
///     <c>/visuauth/admin/external-providers</c>. These overlay the static
///     ones at runtime — DB always wins.</item>
/// </list>
/// </summary>
/// <remarks>
/// Implementations are singletons. The snapshot is populated lazily on the
/// first <see cref="Get"/> call by forcing every registered scheme's
/// <c>IOptionsMonitor&lt;TOptions&gt;</c> to materialise — that's what makes
/// the configurator run and the snapshot grow. Empty for schemes the
/// consumer didn't wire through
/// <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>.
/// </remarks>
public interface IExternalProviderStaticConfigSnapshot
{
    /// <summary>
    /// Returns what the auth handler would have used if the admin had never
    /// touched the dynamic store, or null for schemes that aren't tracked
    /// (un-wired schemes, plus a brief window before the first warmup).
    /// </summary>
    /// <remarks>
    /// Named <c>GetForScheme</c> rather than the natural <c>Get</c> to dodge
    /// CA1716 — <c>Get</c> collides with reserved keywords in some CLR
    /// languages (e.g. VB).
    /// </remarks>
    StaticProviderConfigView? GetForScheme(string scheme);
}

/// <summary>
/// UI-safe view of the pre-overlay credentials. <see cref="ClientId"/> is
/// returned verbatim because it's not a secret; <see cref="HasClientSecret"/>
/// is a boolean only — the static-side secret is never exposed by this
/// snapshot.
/// </summary>
public sealed record StaticProviderConfigView(string? ClientId, bool HasClientSecret);
