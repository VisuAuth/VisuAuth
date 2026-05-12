# PLAN.md — VisuAuth

Living document. Tracks current state, the v0.1 backlog, and the immediate next step.
Updated as PRs land. Long-term direction lives in `CLAUDE.md` section 13 (Roadmap).

---

## Current status

- **Version in development**: v0.1 (pre-alpha)
- **Latest shipped on NuGet**: `VisuAuth 0.0.1-alpha` (placeholder, name reservation)
- **Default branch**: `main` at <https://github.com/VisuAuth/visuauth>
- **Build state**: green (`dotnet build src/VisuAuth.slnx -c Release` → 0 errors, 0 warnings)
- **Test state**: 6 / 6 passing (on `main`; this branch adds more)

---

## Done

### Infrastructure

- Repository bootstrapped: LICENSE (Apache 2.0), README, CONTRIBUTING, SECURITY, `.editorconfig`, `.gitattributes`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`
- Five projects scaffolded (`VisuAuth`, `VisuAuth.Abstractions`, `VisuAuth.Identity`, `VisuAuth.AdminUi`, `VisuAuth.EndUserUi`)
- Solution file at `src/VisuAuth.slnx` with `samples/`, `src/`, `tests/` solution folders
- GitHub Actions: CI workflow (build + test + pack on PR) and Release workflow (NuGet publish on tag)
- PR and issue templates
- NuGet package name `VisuAuth` reserved with placeholder release
- GitHub org `VisuAuth` created
- Domain `visuauth.com` registered
- Branching policy documented (trunk-based, Conventional Commits, squash merge)
- CLAUDE.md and PLAN.md (this file) added

### Public surface

- `VisuAuth.Abstractions`:
  - `IUserStore`, `IRoleStore`, `IAuthenticationFlow`
  - `UserBackendCapabilities` record (the runtime adaptation switch for non-Identity backends)
  - `UserSummary`, `UserDetail`, `UserClaim`, `ExternalLogin`, `UserFilter`, `UserSortBy`, `CreateUserCommand`, `UpdateUserCommand`, `UserResult`, `PagedResult<T>`, `RoleSummary`
  - `ITenantContext`
  - `IMultiTenantEntity` marker
- `VisuAuth.Identity`:
  - `AspNetIdentityUserStore<TUser>` with `ListAsync` (paged + filtered), `GetAsync`, and `GetDetailAsync` (claims, roles, external logins) working end to end
  - DI extension `AddVisuAuthIdentityAdapter<TUser>()`
- `VisuAuth.AdminUi`:
  - Razor Pages library setup verified (pages discovered from the referenced assembly via `AddApplicationPart`)
  - `/visuauth/admin/users` page with htmx-powered live search and pagination
  - `/visuauth/admin/users/{id}` read-only detail page (profile, security, roles, claims, external logins)
  - Default CSS theme with custom property hooks for branding
- `VisuAuth.EndUserUi`:
  - Stub package wired into DI; concrete pages still to come
- `VisuAuth` meta-package:
  - `AddVisuAuth<TUser>()` and `MapVisuAuth()` extension methods
- Sample app:
  - ASP.NET Core Identity + EF Core SQLite + 12 seeded users
  - Drop-in usage proven (`AddVisuAuth<ApplicationUser>` + `MapVisuAuth`)
- Tests:
  - `tests/VisuAuth.AdminUi.Tests` with `WebApplicationFactory` smoke tests covering full-page render, search filtering, and htmx partial response

---

## In flight

### `feat/admin-ui-user-mutations`

Wire the admin mutation actions into the user detail page so operators can
unblock real users from the UI: change email / username / phone, lock /
unlock, reset password (temporary password handed back to the admin), reset
2FA, and revoke active sessions. Each action POSTs to a dedicated handler
and refreshes the page via htmx.

- `AspNetIdentityUserStore<TUser>` implementations for `UpdateAsync`,
  `SetEnabledAsync`, `ResetPasswordAsync`, `ResetTwoFactorAsync`,
  `RevokeSessionsAsync` (currently `NotImplementedException`)
- `UserResult.Metadata` added so password reset can return the generated
  temporary password
- `Detail.cshtml` split into `_DetailContent`, `_ProfileSection`,
  `_SecuritySection` partials with htmx-swappable targets
- Profile section toggles between view and edit modes
- `hx-confirm` on destructive actions
- Tests covering happy path for each action plus a validation failure case

---

## Next up (ordered)

### 1. `feat/admin-ui-create-edit-user`

Forms for creating and editing users. Tenant-aware (when multi-tenancy is enabled, an inactive tenant selector appears for super-admins).

### 2. `feat/multitenant-tenantid-column`

The full multi-tenancy primitive set:

- `IMultiTenantEntity` already exists; add the EF Core global query filter helper
- `SaveChanges` interceptor populating `TenantId` on insert
- `ITenantContext` implementation
- Tenant resolver middleware (subdomain, header, claim — configurable)
- `EnableMultiTenant()` DI extension that flips the switch
- Update `AspNetIdentityUserStore<TUser>` to respect the current tenant
- Per-tenant password policy and lockout config
- Tests with multiple tenants and verified isolation

### 3. `feat/end-user-login-page`

The first end-user UI page: `/visuauth/login` with email + password form, error feedback via htmx, redirect on success. Uses ASP.NET Identity's `SignInManager`.

### 4. `feat/end-user-register-and-reset`

Registration, forgot password, reset password, confirm email, logout. Each is its own page; they share a layout.

### 5. `feat/mobile-rest-api-and-jwt`

`POST /visuauth/api/auth/login`, `POST /visuauth/api/auth/register`, `POST /visuauth/api/auth/refresh`, JWT issuance with HS256. WebView callback flow added on top.

### 6. `feat/theming-programmatic-config`

`services.AddVisuAuth().Configure<VisuAuthTheme>(...)` generates CSS variables at runtime, overriding the defaults from `wwwroot/visuauth.css`. View override (layer 3) and per-tenant theme (layer 4) deferred to v0.2.

### 7. `feat/i18n-pt-br-and-en`

`IStringLocalizer` wired up. All hardcoded English strings in views moved to resource files. pt-BR translation added.

### 8. `feat/embedded-htmx-asset`

Replace the htmx CDN reference with an embedded static asset at `wwwroot/htmx.min.js`. Required for offline / air-gapped deployments.

---

## Open decisions

None right now.

---

## Future ideas (no commitment)

- Audit log plugin writing to a separate table with retention policy
- Outbound webhooks on user events
- Bulk CSV import of users
- Cloud-hosted VisuAuth tier (managed offering)
- VS Code / Rider extension for VisuAuth scaffolding
- Theme marketplace
- Migration tool from `Microsoft.AspNetCore.Identity.UI`

---

## Versioning policy

[SemVer](https://semver.org). Until v1.0:

- Minor bumps (0.x) **may** introduce breaking changes; they will be documented in release notes
- Patch bumps (0.x.y) fix bugs only
- All packages in the `VisuAuth.*` family ship at the same version, even when their code did not change, to keep dependency graphs simple

After v1.0, contracts in `VisuAuth.Abstractions` become stable. Breaking changes to abstractions require a major bump.
