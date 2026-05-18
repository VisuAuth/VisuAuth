namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Drops the auth-handler's cached options for an admin-edited scheme so
/// the next sign-in attempt rebuilds them with the freshly-saved
/// <c>ClientId</c> / <c>ClientSecret</c> — no app restart required.
/// </summary>
/// <remarks>
/// Implementation lives in <c>VisuAuth.Identity</c> because the lookup
/// from scheme name to the concrete <c>IOptionsMonitorCache&lt;TOptions&gt;</c>
/// needs a registration table populated by
/// <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>. Calling
/// <see cref="Invalidate"/> for a scheme the consumer never wired through
/// that helper returns false silently — it just means the scheme uses the
/// static <c>AddXxx(o =&gt; ...)</c> options and isn't expected to reload.
/// </remarks>
public interface IExternalProviderOptionsCacheInvalidator
{
    /// <summary>
    /// Drops the cached options for <paramref name="scheme"/>. Returns
    /// <c>true</c> when the scheme is known to the invalidator;
    /// <c>false</c> otherwise (treated as a no-op).
    /// </summary>
    bool Invalidate(string scheme);
}
