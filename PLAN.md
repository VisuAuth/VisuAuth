# PLAN.md — VisuAuth

Living document. Tracks current state, the v0.1 backlog, and the immediate next step.
Updated as PRs land. Long-term direction lives in `CLAUDE.md` section 13 (Roadmap).

---

## Current status

- **Version in development**: v0.1 (pre-alpha)
- **Latest shipped on NuGet**: `VisuAuth 0.0.1-alpha` (placeholder, name reservation)
- **Default branch**: `main` at <https://github.com/VisuAuth/visuauth>
- **Build state**: green (`dotnet build src/VisuAuth.slnx -c Release` → 0 errors, 0 warnings)
- **Test state**: 55 / 55 passing (on `main`; this branch adds more)

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
  - `IUserStore`, `IRoleStore`, `ITenantStore`, `IAuthenticationFlow`
  - `UserBackendCapabilities` record (the runtime adaptation switch for non-Identity backends)
  - `UserSummary`, `UserDetail`, `UserClaim`, `ExternalLogin`, `UserFilter` (with `Role`, `EmailConfirmed`, `TwoFactorEnabled`), `UserSortBy`, `CreateUserCommand`, `UpdateUserCommand`, `UserResult` (with `Metadata`), `PagedResult<T>`, `RoleSummary`, `TenantSummary`, `TenantOptions`
  - `ITenantContext`
  - `IMultiTenantEntity` marker (in `VisuAuth.Identity`)
- `VisuAuth.Identity`:
  - `AspNetIdentityUserStore<TUser>` with `ListAsync` (search + role / status / verified / 2FA filters + pagination), `GetAsync`, `GetDetailAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `SetEnabledAsync` (lock / unlock), `ResetPasswordAsync` (generates a policy-compliant temporary password), `ResetTwoFactorAsync`, `RevokeSessionsAsync`
  - `AspNetIdentityRoleStore<TUser, TRole>` with the full `IRoleStore` surface (list / get / create / rename / delete / getRolesForUser / assign / remove)
  - `AspNetIdentityTenantStore<TUser>` with the full `ITenantStore` surface (list / get / create / rename / delete); member counts via `IgnoreQueryFilters()` so the catalogue sees every tenant regardless of scope
  - `MultiTenantIdentityUser` base, `MultiTenantIdentityDbContext<TUser>` base with global query filter, `TenantSaveChangesInterceptor`, header + cookie tenant resolver middleware
  - `TemporaryPasswordGenerator` (CSPRNG, policy-compliant, avoids visually ambiguous characters)
  - DI extensions `AddVisuAuthIdentityAdapter<TUser>()`, `AddVisuAuthIdentityAdapter<TUser, TRole>()`, `EnableVisuAuthTenancy(opts)`, `EnableVisuAuthTenancy<TDbContext, TUser>(opts)`
- `VisuAuth.AdminUi`:
  - Razor Pages library setup verified (pages discovered from the referenced assembly via `AddApplicationPart`)
  - `/visuauth/admin/users` page with htmx-powered live search, pagination, and role / status / verified / 2FA filters
  - `/visuauth/admin/users/{id}` detail page with inline profile edit, lock / unlock, reset password (one-time temporary password), reset 2FA, revoke sessions, delete, role assign / remove
  - `/visuauth/admin/users/new` create-user form (autogenerates a temporary password when blank, optional role checkboxes)
  - `/visuauth/admin/roles` catalogue with member counts, inline create / rename / delete
  - `/visuauth/admin/tenants` catalogue with member counts, inline create / rename / delete
  - Sidebar tenant switcher (cookie-backed) that scopes every admin view when multi-tenancy is on
  - One-time secret display with click-to-copy widget (vanilla JS, no framework, animated confirmation)
  - Sidebar active state computed from current route; every nav entry now a real link when its backing capability is on
  - Default CSS theme with custom property hooks for branding
- `VisuAuth.EndUserUi`:
  - Stub package wired into DI; concrete pages still to come
- `VisuAuth` meta-package:
  - `AddVisuAuth<TUser>()` and `MapVisuAuth()` extension methods
- Sample app:
  - ASP.NET Core Identity + EF Core SQLite + 12 seeded users
  - Drop-in usage proven (`AddVisuAuth<ApplicationUser>` + `MapVisuAuth`)
- Tests:
  - `tests/VisuAuth.AdminUi.Tests` with `WebApplicationFactory` smoke tests covering list rendering, search, htmx partials, user detail rendering, 404, row → detail link, every mutation action, create / delete, role assign / remove, role catalogue, tenants catalogue, multi-tenancy isolation, filter combinations, and the sidebar tenant switcher (cookie round-trip + open-redirect guard)

---

## In flight

### `feat/end-user-login-page`

First end-user UI page: a public `/visuauth/login` form. Authenticates via
ASP.NET Identity's `SignInManager`, surfaces invalid-credentials / lockout
errors inline, and respects a sanitised `returnUrl`. End-user pages get a
dedicated clean layout (no admin sidebar).

- Promote `VisuAuth.EndUserUi` from stub to a real Razor Pages library
  (mirroring `VisuAuth.AdminUi` setup with `AddApplicationPart`)
- `/visuauth/login` page with email + password + remember-me + return-url
- Capability-aware: only renders when `UserBackendCapabilities.SupportsLocalLogin`;
  otherwise shows a "this backend does not support local sign-in" message
  (paves the way for the Entra adapter)
- `/visuauth/logout` POST endpoint
- End-user layout `Pages/Shared/_Layout.cshtml` — centered card, brand,
  no nav
- Sample app wires `app.UseAuthentication()` / `UseAuthorization()` and
  links to the new URLs from the home page
- Tests for: successful sign-in (cookie set, redirect to return-url),
  wrong password (error rendered, no cookie), open-redirect guard,
  unsupported-backend message

**Out of scope** (deferred):

- Registration, forgot-password, reset-password, confirm-email — land
  together in `feat/end-user-register-and-reset`
- Two-factor challenge page — needs TOTP plumbing, ships with the
  external providers / 2FA PR
- External login buttons (Google, Microsoft, Apple)

---

## Next up (ordered)

### 1. `feat/end-user-register-and-reset`

### 2. `feat/end-user-register-and-reset`

Registration, forgot password, reset password, confirm email. Each is its
own page; they share the end-user layout introduced with the login PR.

### 3. `feat/mobile-rest-api-and-jwt`

`POST /visuauth/api/auth/login`, `POST /visuauth/api/auth/register`, `POST /visuauth/api/auth/refresh`, JWT issuance with HS256. WebView callback flow added on top.

### 4. `feat/theming-programmatic-config`

`services.AddVisuAuth().Configure<VisuAuthTheme>(...)` generates CSS variables at runtime, overriding the defaults from `wwwroot/visuauth.css`. View override (layer 3) and per-tenant theme (layer 4) deferred to v0.2.

### 5. `feat/i18n-pt-br-and-en`

`IStringLocalizer` wired up. All hardcoded English strings in views moved to resource files. pt-BR translation added.

### 6. `feat/embedded-htmx-asset`

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
