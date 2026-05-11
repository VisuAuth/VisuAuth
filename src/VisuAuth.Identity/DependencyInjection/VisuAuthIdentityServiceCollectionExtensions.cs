using Microsoft.Extensions.DependencyInjection;

namespace VisuAuth.Identity.DependencyInjection;

/// <summary>
/// Registration helpers for the ASP.NET Core Identity adapter.
/// </summary>
public static class VisuAuthIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Wires VisuAuth's user/role/auth stores to ASP.NET Core Identity. Call after
    /// <c>services.AddIdentity&lt;TUser, TRole&gt;()</c> (or the equivalent
    /// configuration of <see cref="Microsoft.AspNetCore.Identity"/>).
    /// </summary>
    public static IServiceCollection AddVisuAuthIdentityAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Concrete IUserStore / IRoleStore / IAuthenticationFlow implementations
        // backed by UserManager / SignInManager / RoleManager are registered here
        // once they are implemented. Pre-alpha placeholder.
        return services;
    }
}
