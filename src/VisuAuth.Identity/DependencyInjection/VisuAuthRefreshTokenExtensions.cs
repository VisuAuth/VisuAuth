using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;

namespace VisuAuth.Identity.DependencyInjection;

/// <summary>
/// Registration helpers for the opt-in refresh-token plugin. Without these
/// calls <see cref="NoOpRefreshTokenService"/> handles every
/// <see cref="IRefreshTokenService"/> call, so the auth API can inject it
/// unconditionally.
/// </summary>
public static class VisuAuthRefreshTokenExtensions
{
    /// <summary>
    /// Registers the no-op service as the default. Wired automatically inside
    /// <c>AddVisuAuth().UseAspNetIdentity()</c>.
    /// </summary>
    internal static IServiceCollection AddVisuAuthRefreshTokenDefault(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IRefreshTokenService, NoOpRefreshTokenService>();
        return services;
    }

    /// <summary>
    /// Turns on opaque, single-use, rotating refresh tokens with replay
    /// detection, replacing the default no-op with
    /// <see cref="EfCoreRefreshTokenStore"/>.
    /// <para>
    /// This <b>changes the contract of <c>POST /visuauth/api/auth/refresh</c></b>:
    /// once enabled it expects <c>{ "refreshToken": "..." }</c> in the body and
    /// no longer reissues from the access token in the <c>Authorization</c>
    /// header. That is the point — leaving the old path open would let an
    /// attacker with a leaked access token simply keep using it. Sign-in and
    /// registration responses start returning a <c>refreshToken</c>.
    /// </para>
    /// <para>
    /// Requires a metadata DbContext (the same one tenancy uses) and the
    /// <c>VisuAuthRefreshTokens</c> table — add a migration after enabling.
    /// </para>
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configure">Optional callback to tweak
    /// <see cref="RefreshTokenOptions"/> — e.g. <c>opts.Lifetime = TimeSpan.FromDays(14)</c>.</param>
    public static IServiceCollection AddVisuAuthRefreshTokens(
        this IServiceCollection services,
        Action<RefreshTokenOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RefreshTokenOptions>();
        }

        services.RemoveAll<IRefreshTokenService>();
        services.AddScoped<IRefreshTokenService, EfCoreRefreshTokenStore>();

        return services;
    }
}
