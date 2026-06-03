# VisuAuth.Abstractions

The public contracts and capability model for [VisuAuth](https://github.com/VisuAuth/visuauth).

Reference this package to write **adapters** (a backend implementation such as
ASP.NET Core Identity or Microsoft Entra) or **extensions** that plug into
VisuAuth's admin UI, authentication flows, and theming — without depending on
any concrete adapter.

## What's in here

Backend-agnostic contracts and the DTOs they exchange:

- **Stores** — `IUserStore`, `IRoleStore`, `ITenantStore`
- **Flows** — `IAuthenticationFlow`, `ITwoFactorFlow`, `IExternalLoginFlow`, `IJwtIssuer`
- **Capabilities** — `UserBackendCapabilities` (the runtime feature switch the UI consults to adapt to each backend)
- **Auditing** — `IAuditReader`, `IAuditWriter`, `AuditEvent`, `AuditActions`
- **Configuration** — `IAdapterConfigStore`, `IAdapterConfigSchema`, `IExternalProviderConfigStore`
- **Tenancy** — `ITenantContext`, `TenantOptions`
- **Shared shapes** — `StoreResult` (the result of a store/flow operation), `PagedResult<T>`, and the user/role/tenant DTOs

Every method that does I/O is `async` and takes a `CancellationToken`. Expected
business errors (validation, conflicts, missing records) are returned as a
`StoreResult` rather than thrown — exceptions are reserved for programmer errors
and operations a backend genuinely does not support (which throw
`NotSupportedException`, gated by `UserBackendCapabilities`).

## Stability guarantee

`VisuAuth.Abstractions` is the contract every adapter and extension builds on,
so its public surface is the one part of VisuAuth held to the strictest
compatibility bar.

- **From v1.0 onward, the public surface of `VisuAuth.Abstractions` is stable.**
  Any breaking change to it (renaming or removing a type or member, changing a
  signature, tightening a nullability annotation) requires a **major** version
  bump and is called out in the release notes.
- **Additive, source-compatible changes** (a new optional member with a default,
  a new type, a new capability flag that defaults to the safe value) ship in
  **minor** releases.
- **Before v1.0** the surface may still change between minor (`0.x`) releases —
  that window is where naming/shape nits get resolved (e.g. `UserResult` →
  `StoreResult`). Such changes are documented in the release notes.

This follows the project's [SemVer](https://semver.org) policy; see
[`PLAN.md`](https://github.com/VisuAuth/visuauth/blob/main/PLAN.md#versioning-policy)
for the full versioning rules and `CHANGELOG.md` for the per-release detail.

## License

Apache-2.0.
