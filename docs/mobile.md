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

- HS256 with a symmetric key from configuration (`VisuAuth:Jwt:SigningKey`).
- Claims: `sub` (user id), `email`, `tenant_id` (when multi-tenant), `roles`,
  `exp`.
- Default lifetime one hour, configurable.
- No discovery endpoint, no JWKS, no rotation — by design. VisuAuth is not an
  OIDC server; pair it with Duende IdentityServer or similar if you need one.

## Planned outline

- End-to-end REST login example with a sample request/response.
- WebView callback: fragment vs query placement, the disallowed-scheme
  fallback.
- Configuring the signing key and token lifetime.
- Validating the token on your API.
