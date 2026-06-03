# Mobile & JWT

VisuAuth supports two mobile flows over the same backend and the same JWT
issuer — the only difference is how the request arrives.

> **This page is being expanded for the v1.0 documentation site.**

## The two flows

- **REST API.** The mobile app builds its own native UI, POSTs credentials to
  `/visuauth/api/auth/login`, and receives a JWT. Use this for a 100% native
  experience.
- **WebView.** The app opens an in-app browser at
  `/visuauth/login?return=app://callback`. After authentication the server
  redirects to the deep link with the JWT. Use this to reuse the same themed
  pages — including external providers — as the web flow.

## JWT

Register the issuer with `AddVisuAuthJwt<TUser>(…)` and configure it through
`JwtOptions` in the lambda — there is no built-in configuration-binding key, so
load secrets yourself (here from a `VisuAuth:Jwt:SigningKey` config entry that
*you* define):

```csharp
using VisuAuth.Identity.Authentication;

builder.Services.AddVisuAuthJwt<ApplicationUser>(options =>
{
    options.SigningKey = builder.Configuration["VisuAuth:Jwt:SigningKey"]!; // 32+ UTF-8 bytes
    options.Issuer = "VisuAuth";        // iss claim   (default "VisuAuth")
    options.Audience = "VisuAuth";      // aud claim   (default "VisuAuth")
    options.LifetimeMinutes = 60;       // token lifetime (default 60)
    options.ClockSkewMinutes = 5;       // validation skew (default 5)
});
```

This also adds the JWT bearer authentication scheme, so the same token
authenticates callers against any `[Authorize]`-protected endpoint you mount.

- **HS256** with a symmetric signing key (must be at least 32 UTF-8 bytes — the
  issuer throws at startup on a shorter key).
- Claims: `sub` (user id), `email`, `tenant_id` (when multi-tenant), `roles`,
  `exp`.
- Default lifetime 60 minutes, configurable via `JwtOptions.LifetimeMinutes`.
- No discovery endpoint, no JWKS, no rotation — by design. VisuAuth is not an
  OIDC server; pair it with Duende IdentityServer or similar if you need one.

> `AddVisuAuthJwt` is **required** for the end-user sign-in pages too, not just
> the mobile API — see [Getting started](getting-started.md).

## Planned outline

- End-to-end REST login example with a sample request/response.
- WebView callback: fragment vs query placement, the disallowed-scheme
  fallback.
- Configuring the signing key and token lifetime.
- Validating the token on your API.
