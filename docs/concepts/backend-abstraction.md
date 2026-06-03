# Backend abstraction & capabilities

VisuAuth's single most important architectural decision is that the UI talks
to **backend-agnostic contracts**, never to a specific identity system. The
ASP.NET Core Identity adapter, the Microsoft Entra ID adapter, and the
Microsoft Entra External ID adapter all plug into the same interfaces defined
in `VisuAuth.Abstractions`.

The UI inspects a **capability bag** (`UserBackendCapabilities`) at runtime and
adapts: a backend that does not own the login form (Entra) automatically gets a
"Sign in with Microsoft" button instead of an email/password form.

> **This page is being expanded for the v1.0 documentation site.**
> The package-level contract reference already lives in
> [`src/VisuAuth.Abstractions/README.md`](https://github.com/VisuAuth/visuauth/blob/main/src/VisuAuth.Abstractions/README.md),
> which carries the v1.0 stability guarantee.

## Planned outline

- The core contracts: `IUserStore`, `IRoleStore`, `IAuthenticationFlow`,
  `IJwtIssuer`, `ITenantStore`.
- `UserBackendCapabilities` — every flag, what it gates in the UI, and how
  unsupported operations throw `NotSupportedException` rather than silently
  succeed.
- `StoreResult` — the result-style return shape for expected errors.
- DTO shapes: `UserSummary`, `UserDetail`, `CreateUserCommand`,
  `UserFilter`, `PagedResult<T>`.
- How to write your own adapter.
