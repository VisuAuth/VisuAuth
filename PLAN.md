# PLAN.md — VisuAuth

Living document. Tracks current state, the v0.1 backlog, and the immediate next step.
Updated as PRs land. Long-term direction lives in `CLAUDE.md` section 13 (Roadmap).

---

## Current status

- **Version in development**: v0.1 (pre-alpha)
- **Latest shipped on NuGet**: `VisuAuth 0.0.1-alpha` (placeholder, name reservation)
- **Default branch**: `main` at <https://github.com/VisuAuth/visuauth>
- **Build state**: green (`dotnet build src/VisuAuth.slnx -c Release` → 0 errors, 0 warnings)
- **Test state**: 19 / 19 passing (on `main`; this branch adds more)

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
  - `UserSummary`, `UserDetail`, `UserClaim`, `ExternalLogin`, `UserFilter`, `UserSortBy`, `CreateUserCommand`, `UpdateUserCommand`, `UserResult` (with `Metadata`), `PagedResult<T>`, `RoleSummary`
  - `ITenantContext`
  - `IMultiTenantEntity` marker
- `VisuAuth.Identity`:
  - `AspNetIdentityUserStore<TUser>` with `ListAsync`, `GetAsync`, `GetDetailAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `SetEnabledAsync` (lock / unlock), `ResetPasswordAsync` (generates a policy-compliant temporary password), `ResetTwoFactorAsync`, `RevokeSessionsAsync`
  - `TemporaryPasswordGenerator` (CSPRNG, policy-compliant, avoids visually ambiguous characters)
  - DI extension `AddVisuAuthIdentityAdapter<TUser>()`
- `VisuAuth.AdminUi`:
  - Razor Pages library setup verified (pages discovered from the referenced assembly via `AddApplicationPart`)
  - `/visuauth/admin/users` page with htmx-powered live search and pagination
  - `/visuauth/admin/users/{id}` detail page with inline profile edit, lock / unlock, reset password (one-time temporary password), reset 2FA, revoke sessions, delete
  - `/visuauth/admin/users/new` create-user form (autogenerates a temporary password when blank)
  - One-time secret display with click-to-copy widget (vanilla JS, no framework, animated confirmation)
  - Default CSS theme with custom property hooks for branding
- `VisuAuth.EndUserUi`:
  - Stub package wired into DI; concrete pages still to come
- `VisuAuth` meta-package:
  - `AddVisuAuth<TUser>()` and `MapVisuAuth()` extension methods
- Sample app:
  - ASP.NET Core Identity + EF Core SQLite + 12 seeded users
  - Drop-in usage proven (`AddVisuAuth<ApplicationUser>` + `MapVisuAuth`)
- Tests:
  - `tests/VisuAuth.AdminUi.Tests` with `WebApplicationFactory` smoke tests covering list rendering, search, htmx partials, user detail rendering, 404, row → detail link, and every mutation action (lock / unlock / reset password / reset 2FA / revoke sessions / profile update + validation failure)

---

## In flight

### `feat/admin-ui-role-management`

Wire the role store adapter and let admins assign / remove roles from
the user detail page. The Roles section becomes interactive: each chip
is removable, and a dropdown attaches any of the roles already present
in the backend.

- `AspNetIdentityRoleStore<TUser, TRole>` implementing the full `IRoleStore`
  surface (list, get, create, rename, delete, getRolesForUser, assign,
  remove)
- `AddVisuAuthIdentityAdapter<TUser, TRole>` and `AddVisuAuth<TUser, TRole>`
  overloads, with backward-compatible defaults to `IdentityRole`
- `_RolesSection.cshtml` partial: removable chips + assign dropdown
- `OnPostAssignRoleAsync` / `OnPostRemoveRoleAsync` handlers on the
  detail page (htmx swap of the detail content)
- Sample app seeds three roles (`Admin`, `Manager`, `Support`) and pins
  a couple onto seeded users so the dashboard demonstrates the feature
- Tests for the store (assign / remove / duplicates) and for the UI
  (chip rendering, dropdown filtering, full round-trip)

**Out of scope** (deferred to `feat/admin-ui-roles-catalogue`):

- `/visuauth/admin/roles` page with member counts and inline CRUD on the
  role catalogue itself
- Role checkboxes on the create-user form

---

## Next up (ordered)

### 1. `feat/admin-ui-roles-catalogue`

`/visuauth/admin/roles` page listing every role with a member count,
plus inline create / rename / delete. Surfaces role checkboxes on the
create-user form so admins can assign roles at creation time.

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
