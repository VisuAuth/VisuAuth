# PLAN.md — VisuAuth

Living document. Tracks current state, the v0.1 backlog, and the immediate next step.
Updated as PRs land. Long-term direction lives in `CLAUDE.md` section 13 (Roadmap).

---

## Current status

- **Version in development**: v0.1 (pre-alpha)
- **Latest shipped on NuGet**: `VisuAuth 0.0.1-alpha` (placeholder, name reservation)
- **Default branch**: `main` at <https://github.com/VisuAuth/visuauth>
- **Build state**: green (`dotnet build src/VisuAuth.slnx -c Release` → 0 errors, 0 warnings)
- **Test state**: 78 / 78 passing (on `main`; this branch adds more)

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

### `feat/mobile-rest-api-and-jwt`

Open the mobile / native-API channel: three minimal-API endpoints under
`/visuauth/api/auth` issuing JWTs signed with HS256. No IAM-server features
— no discovery, no JWKS, no key rotation — just enough to let a mobile
client (or any non-cookie client) authenticate against the same Identity
backing store the admin UI uses.

- `VisuAuth.Abstractions`: `IJwtIssuer`, `JwtOptions` (signing key, issuer,
  audience, lifetime), `JwtTokenResult` DTO
- `VisuAuth.Identity`: `AspNetIdentityJwtIssuer<TUser>` — builds claims
  (`sub`, `email`, `tenant_id`, `roles`, `security_stamp`, `iss`, `aud`,
  `exp`); HS256 via `System.IdentityModel.Tokens.Jwt`
- `VisuAuth.EndUserUi`: minimal-API group `/visuauth/api/auth` with
  `POST /login`, `POST /register`, `POST /refresh`. Refresh validates the
  bearer (even if expired) against the current security stamp — no
  separate refresh-token table needed in v0.1
- DI: `AddVisuAuthJwt(Action<JwtOptions>)` registers the issuer and wires
  `AddJwtBearer` so the same JWT also authenticates against any
  `[Authorize]`-protected endpoint the consumer adds
- Sample app: configures a dev signing key in `Program.cs`, documents
  curl examples on the home page
- Tests for happy login, wrong-password 401, register flow, refresh with
  valid / stale / wrong-stamp tokens, tenant-id claim presence

**Out of scope** (deferred):

- WebView deep-link callback (`?returnUrl=app://...` triggers a JWT
  redirect) — ships in `feat/mobile-webview-callback`
- Refresh-token table with explicit revocation — security stamp covers the
  common case (admin clicks "revoke sessions" → next refresh fails)
- JWKS / discovery endpoint — VisuAuth is not an OIDC server; consumers
  needing OIDC pair with Duende IdentityServer
- Key rotation — single static key per deployment in v0.1

---

## Next up (ordered)

### 1. `feat/mobile-webview-callback`

In-app browser flow: `/visuauth/login?returnUrl=app://callback` redirects
to the deep link with the JWT appended on success. Lets mobile apps reuse
the themed login pages (and future external providers) instead of
building a native form.

### 2. `feat/theming-programmatic-config`

`services.AddVisuAuth().Configure<VisuAuthTheme>(...)` generates CSS variables at runtime, overriding the defaults from `wwwroot/visuauth.css`. View override (layer 3) and per-tenant theme (layer 4) deferred to v0.2.

### 3. `feat/i18n-pt-br-and-en`

`IStringLocalizer` wired up. All hardcoded English strings in views moved to resource files. pt-BR translation added.

### 4. `feat/embedded-htmx-asset`

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
