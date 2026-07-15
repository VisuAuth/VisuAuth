using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace VisuAuth.AdminUi.DependencyInjection;

/// <summary>
/// Refuses to start when the admin dashboard is gated behind "requires an
/// authenticated user" but the host registered no authentication scheme to
/// challenge with.
/// </summary>
/// <remarks>
/// <para>
/// Without this, the misconfiguration is invisible until someone opens
/// <c>/visuauth/admin</c> in production and every request dies with
/// <c>InvalidOperationException: No authenticationScheme was specified, and
/// there was no DefaultChallengeScheme found</c> — a 500 that reads like a bug
/// in VisuAuth rather than a missing line in <c>Program.cs</c>.
/// </para>
/// <para>
/// It is an easy state to reach: the Entra adapter authenticates the
/// <em>app</em> to Graph with app-only credentials and registers no scheme for
/// <em>humans</em>, so a consumer can wire a complete-looking Entra app and
/// never notice they have no way to sign in.
/// </para>
/// <para>
/// The check is skipped when the resolved admin policy does not deny anonymous
/// users — i.e. when the consumer deliberately called
/// <see cref="VisuAuthAdminUiServiceCollectionExtensions.AllowAnonymousVisuAuthAdmin"/>
/// or registered their own permissive policy.
/// </para>
/// </remarks>
internal sealed class VisuAuthAdminAuthenticationStartupCheck(
    IOptions<AuthorizationOptions> authorizationOptions,
    IOptions<AuthenticationOptions> authenticationOptions) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (RequiresAuthenticatedUser() && !HasDefaultChallengeScheme())
        {
            throw new InvalidOperationException(
                "VisuAuth's admin dashboard requires an authenticated user (the " +
                $"'{VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy}' authorization " +
                "policy), but no default authentication scheme is registered, so it has nothing to " +
                "challenge with — every request to /visuauth/admin would fail with " +
                "\"No authenticationScheme was specified, and there was no DefaultChallengeScheme found\". " +
                "Do one of the following:" +
                "\n  * Register an authentication scheme. ASP.NET Core Identity (AddIdentity / " +
                "AddDefaultIdentity) sets one up for you." +
                "\n  * For Microsoft Entra ID, add the VisuAuth.Entra.Web package and call " +
                "AddVisuAuthEntraSignIn(...) — AddVisuAuthEntra alone only authenticates the app to " +
                "Microsoft Graph, it does not sign operators in." +
                "\n  * If you registered a scheme but no default, set " +
                "AuthenticationOptions.DefaultChallengeScheme." +
                "\n  * If the dashboard is already protected another way (an upstream gateway, network " +
                "isolation), call services.AllowAnonymousVisuAuthAdmin() to drop VisuAuth's gate " +
                "deliberately.");
        }

        return next;
    }

    private bool RequiresAuthenticatedUser()
        => authorizationOptions.Value
            .GetPolicy(VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy)
            ?.Requirements
            .OfType<DenyAnonymousAuthorizationRequirement>()
            .Any() == true;

    /// <summary>
    /// Mirrors what <c>AuthenticationService.ChallengeAsync</c> resolves when no
    /// scheme is named: the challenge default, falling back to the global one.
    /// If both are unset the framework throws — which is the failure this guard
    /// exists to pre-empt.
    /// </summary>
    private bool HasDefaultChallengeScheme()
        => !string.IsNullOrEmpty(authenticationOptions.Value.DefaultChallengeScheme)
           || !string.IsNullOrEmpty(authenticationOptions.Value.DefaultScheme);
}
