using Microsoft.AspNetCore.Authorization;

namespace VisuAuth.AdminUi.DependencyInjection;

/// <summary>
/// Configures how the admin dashboard at <c>/visuauth/admin</c> is gated.
/// Pass a callback to <c>AddAdminUi(...)</c> / <c>AddVisuAuthAdminUi(...)</c>:
/// <code>
/// .AddAdminUi(admin => admin.RequireRole("Admin"))
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Without any configuration the dashboard requires an <b>authenticated user</b>
/// — secure by default, but usually looser than you want, since every signed-in
/// end user qualifies. <see cref="RequireRole"/> is the one-liner for the common
/// case; <see cref="ConfigurePolicy"/> takes anything an
/// <see cref="AuthorizationPolicyBuilder"/> can express.
/// </para>
/// <para>
/// Registering an authorization policy named
/// <see cref="VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy"/>
/// yourself still works and still wins — this type exists so the common cases
/// don't require knowing that name.
/// </para>
/// </remarks>
public sealed class VisuAuthAdminOptions
{
    private Action<AuthorizationPolicyBuilder>? _configurePolicy;

    /// <summary>
    /// Restricts the dashboard to users in any of the given roles (on top of
    /// requiring authentication). The common case, and equivalent to
    /// <c>ConfigurePolicy(p =&gt; p.RequireAuthenticatedUser().RequireRole(roles))</c>.
    /// </summary>
    /// <param name="roles">
    /// Role names; a user in <em>any</em> of them passes, matching
    /// <see cref="AuthorizationPolicyBuilder.RequireRole(string[])"/>. Must not
    /// be empty — an empty role list would silently admit nobody.
    /// </param>
    public VisuAuthAdminOptions RequireRole(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Length == 0)
        {
            throw new ArgumentException(
                "Specify at least one role. An empty role list would deny every user, " +
                "including administrators.",
                nameof(roles));
        }

        return ConfigurePolicy(policy => policy.RequireAuthenticatedUser().RequireRole(roles));
    }

    /// <summary>
    /// Full control over the admin policy — claims, custom requirements,
    /// assertions. Replaces any previously configured policy, including one set
    /// by <see cref="RequireRole"/>.
    /// </summary>
    public VisuAuthAdminOptions ConfigurePolicy(Action<AuthorizationPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configurePolicy = configure;
        return this;
    }

    /// <summary>
    /// Whether a policy was configured, i.e. whether the "authenticated user"
    /// default should be replaced.
    /// </summary>
    internal bool HasPolicy => _configurePolicy is not null;

    /// <summary>Applies the configured policy. Only valid when <see cref="HasPolicy"/>.</summary>
    internal void Apply(AuthorizationPolicyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _configurePolicy?.Invoke(builder);
    }
}
