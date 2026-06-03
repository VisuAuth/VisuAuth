# Microsoft Entra ID adapter

The `VisuAuth.Entra` adapter puts the VisuAuth admin UI in front of
**Microsoft Entra ID** (workforce tenants) via Microsoft Graph — a friendlier
operations surface than the Azure Portal for day-to-day user and role
management.

Because Entra owns the login experience, the adapter declares
`SupportsLocalLogin = false`; the end-user UI swaps the email/password form for
a "Sign in with Microsoft" button automatically (see
[Backend abstraction](../concepts/backend-abstraction.md)).

> **This page is being expanded for the v1.0 documentation site.**
> The package-level reference already lives in
> [`src/VisuAuth.Entra/README.md`](https://github.com/VisuAuth/visuauth/blob/main/src/VisuAuth.Entra/README.md).

## Planned outline

- App registration and the Graph permissions the adapter needs.
- Configuring `EntraOptions` (tenant id, client id/secret, app-role resource).
- What the adapter supports vs. delegates (capabilities).
- Role management via app roles and service principals.
- The audit reader backed by Entra directory audits.
- TOTP / authentication-method reset.
