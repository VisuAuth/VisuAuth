using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using VisuAuth.Abstractions.Users;
using VisuAuth.Identity.Users;

namespace VisuAuth.Identity.DependencyInjection;

/// <summary>
/// Registration helpers for the ASP.NET Core Identity adapter.
/// </summary>
public static class VisuAuthIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Wires VisuAuth's user store to ASP.NET Core Identity using the supplied
    /// user type. Call after <c>AddIdentity&lt;TUser, TRole&gt;()</c> and the
    /// corresponding EF Core stores are configured.
    /// </summary>
    /// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
    public static IServiceCollection AddVisuAuthIdentityAdapter<TUser>(this IServiceCollection services)
        where TUser : IdentityUser
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUserStore, AspNetIdentityUserStore<TUser>>();

        return services;
    }
}
