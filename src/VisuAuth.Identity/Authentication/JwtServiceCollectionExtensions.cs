using System.IdentityModel.Tokens.Jwt;
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

        // Validation accepts the primary signing key plus any rotated-out keys,
        // so a rotation can retire a secret without invalidating tokens still in
        // flight. The issuer only ever signs with the primary SigningKey.
        var validationKeys = BuildValidationKeys(snapshot);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = snapshot.Issuer,
            ValidateAudience = true,
            ValidAudience = snapshot.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = validationKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(snapshot.ClockSkewMinutes),
            NameClaimType = "sub",
        };

        // The refresh endpoint re-authenticates a (possibly expired) token
        // before minting a fresh one. Give it a validator built from the very
        // same parameters as the bearer scheme so signature / issuer /
        // audience checks can never drift apart; the validator clones these
        // and turns the lifetime check off (expired tokens are the whole
        // point of refresh).
        services.AddSingleton<IJwtValidator>(new AspNetIdentityJwtValidator(validationParameters));

        services
            .AddAuthentication()
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = validationParameters;

                if (snapshot.ValidateSecurityStamp)
                {
                    // Revocation gate: compare the token's baked-in security
                    // stamp against the user's current one. Rotating the stamp
                    // (lockout, "revoke sessions", password change) then
                    // invalidates every outstanding token on its next use,
                    // rather than leaving it valid until exp.
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context => ValidateSecurityStampAsync<TUser>(context),
                    };
                }
            });

        return services;
    }

    /// <summary>
    /// Builds the list of keys the bearer scheme and the refresh validator will
    /// accept: the primary <see cref="JwtOptions.SigningKey"/> first, then any
    /// <see cref="JwtOptions.AdditionalValidationKeys"/>. Every key must clear
    /// the HS256 minimum (256 bits / 32 UTF-8 bytes); surfacing that at startup
    /// beats an opaque IdentityModel failure deep in the middleware.
    /// </summary>
    private static List<SecurityKey> BuildValidationKeys(JwtOptions options)
    {
        var keys = new List<SecurityKey> { CreateHs256Key(options.SigningKey, nameof(JwtOptions.SigningKey)) };

        foreach (var additional in options.AdditionalValidationKeys)
        {
            keys.Add(CreateHs256Key(additional, nameof(JwtOptions.AdditionalValidationKeys)));
        }

        return keys;
    }

    private static SymmetricSecurityKey CreateHs256Key(string key, string source)
    {
        var bytes = Encoding.UTF8.GetBytes(key ?? string.Empty);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"VisuAuth JwtOptions.{source} must be at least 32 UTF-8 bytes for HS256. " +
                "Configure a long random secret in your secret store.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    /// <summary>
    /// Fails token validation when the presented <c>visuauth_stamp</c> claim
    /// no longer matches the user's current security stamp, or when the user
    /// can no longer be found. Wired as the bearer <c>OnTokenValidated</c>
    /// event when <see cref="JwtOptions.ValidateSecurityStamp"/> is on.
    /// </summary>
    private static async Task ValidateSecurityStampAsync<TUser>(TokenValidatedContext context)
        where TUser : IdentityUser
    {
        var principal = context.Principal;
        var userId = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            context.Fail("Token is missing the subject claim.");
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<TUser>>();
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            context.Fail("User no longer exists.");
            return;
        }

        var tokenStamp = principal!.FindFirst(AspNetIdentityJwtIssuer<TUser>.SecurityStampClaimType)?.Value;
        var currentStamp = await userManager.GetSecurityStampAsync(user);
        if (!string.Equals(tokenStamp, currentStamp, StringComparison.Ordinal))
        {
            context.Fail("Security stamp has changed; the token has been revoked.");
        }
    }
}
