using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.Identity.Authentication;

/// <summary>
/// Helpers for plugging the JWT issuer + bearer authentication scheme into
/// a consumer host.
/// </summary>
public static class JwtServiceCollectionExtensions
{
    /// <summary>
    /// Registers the JWT issuer and adds the bearer authentication scheme so
    /// the same access token authenticates mobile / API callers against any
    /// <c>[Authorize]</c>-protected endpoint the consumer mounts.
    /// </summary>
    /// <typeparam name="TUser">The Identity user type used by the consumer.</typeparam>
    public static IServiceCollection AddVisuAuthJwt<TUser>(
        this IServiceCollection services,
        Action<JwtOptions> configure)
        where TUser : IdentityUser
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddScoped<IJwtIssuer, AspNetIdentityJwtIssuer<TUser>>();

        // Bind the options once at registration time so we can wire the
        // bearer validation parameters with the same key the issuer uses.
        var snapshot = new JwtOptions();
        configure(snapshot);

        var keyBytes = Encoding.UTF8.GetBytes(snapshot.SigningKey);

        services
            .AddAuthentication()
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = snapshot.Issuer,
                    ValidateAudience = true,
                    ValidAudience = snapshot.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(snapshot.ClockSkewMinutes),
                    NameClaimType = "sub",
                };
            });

        return services;
    }
}
