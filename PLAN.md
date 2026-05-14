# PLAN.md — VisuAuth

Living document. Tracks current state, the v0.1 backlog, and the immediate next step.
Updated as PRs land. Long-term direction lives in `CLAUDE.md` section 13 (Roadmap).

---

## Current status

- **Version in development**: v0.1 (pre-alpha)
- **Latest shipped on NuGet**: `VisuAuth 0.0.1-alpha` (placeholder, name reservation)
- **Default branch**: `main` at <https://github.com/VisuAuth/visuauth>
- **Build state**: green (`dotnet build src/VisuAuth.slnx -c Release` → 0 errors, 0 warnings)
- **Test state**: green on `main` (split unit + integration projects; this branch adds more)

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
  - Programmatic theming (`Configure<VisuAuthTheme>(...)`) emits CSS custom property overrides through the `<va-theme-style />` tag helper; sample app ships preset palettes (`Default`, `Purple`, `Orange`, `Forest`, `Midnight`, `Serif`)
  - i18n pipeline: JSON-backed `IStringLocalizer<AdminSharedResources>` / `IStringLocalizer<EndUserSharedResources>` (provider: `My.Extensions.Localization.Json`), `en` + `pt-BR` translations, query / cookie / header culture resolution, `POST /visuauth/culture` switch endpoint with open-redirect guard, `<va-language-switcher />` tag helper in both layouts
  - htmx 2.0.4 shipped as an embedded static asset (`VisuAuth.AdminUi/wwwroot/htmx.min.js`); both layouts reference `/_content/VisuAuth.AdminUi/htmx.min.js` so air-gapped deployments work without an outbound CDN call
  - View / page override (theming layer 3) — drop a same-named `.cshtml` in `/Views/VisuAuth/` (root configurable via `VisuAuthViewOverrideOptions`) and Razor uses it instead of the package default; covers partials + layouts via `IViewLocationExpander` and full Razor Pages via `IPageRouteModelConvention` that demotes our routes so a consumer page at the same `@page` route wins
- `VisuAuth.EndUserUi`:
  - `/visuauth/login` and `/visuauth/logout` Razor Pages (email + password + remember-me, anti-forgery, redirect-back to `returnUrl`)
  - `/visuauth/register`, `/visuauth/forgot-password`, `/visuauth/reset-password`, `/visuauth/confirm-email` Razor Pages
  - `/visuauth/api/auth/{login,register,refresh}` endpoints issuing HS256 JWTs (`sub`, `email`, `tenant_id`, `roles`, `exp`) — mobile / native channel
  - WebView deep-link callback: when `returnUrl` parses to an allow-listed non-HTTP scheme, login redirects to it with the JWT in the URL fragment; opt-in preview page for desktop dev testing
  - Shared `<va-password>` and `<va-form-errors>` tag helpers + `visuauth.js` show/hide password widget
  - Layout reuses the admin CSS for visual continuity
- `VisuAuth` meta-package:
  - `AddVisuAuth<TUser>()` and `MapVisuAuth()` extension methods
- Sample app:
  - ASP.NET Core Identity + EF Core SQLite + 12 seeded users
  - Drop-in usage proven (`AddVisuAuth<ApplicationUser>` + `MapVisuAuth`)
- Tests:
  - `tests/VisuAuth.UnitTests` and `tests/VisuAuth.IntegrationTests` projects, with shared method-naming convention `Method_Scenario_ExpectedResult`
  - Integration coverage (via `WebApplicationFactory<Program>`): admin list rendering, search, htmx partials, user detail, 404, mutation actions, create / delete, role assign / remove, role catalogue, tenants catalogue, multi-tenancy isolation, filter combinations, sidebar tenant switcher (cookie + open-redirect guard), end-user login / register / reset / confirm, JWT API, WebView callback (preview page, fragment / query placement, disallowed scheme fallback), password show/hide toggle markup
  - Unit coverage: `TemporaryPasswordGenerator`, `UserResult`, tenant context helpers, `<va-password>` and `<va-form-errors>` tag helpers
  - Local Sonar scan wired via `scripts/sonar-local.ps1` against the same OpenCover coverage emitted on CI

---

## In flight

### `feat/theming-per-tenant`

Theming layer 4 from CLAUDE.md §8.4 — when multi-tenancy is on, the
brand palette can vary per tenant. Sits cleanly on top of layer 2
(programmatic `VisuAuthTheme`): the consumer implements one resolver
contract, VisuAuth handles the per-request lookup and merging.

- Contract: `ITenantThemeResolver` (in `VisuAuth.AdminUi.Theming`)
  with one method
  `Task<VisuAuthTheme?> ResolveAsync(string? tenantId, CancellationToken)`.
  Returning `null` means "no per-tenant override" — the global
  `IOptions<VisuAuthTheme>` applies as before.
- Default registration: `NoOpTenantThemeResolver` (returns `null`)
  goes in via `TryAdd` so single-tenant deployments and consumers
  who never opt in stay on the existing fast path.
- Merging: `VisuAuthThemeMerger.Merge(tenant, global)` overlays
  tenant on global property-by-property — tenant wins where set,
  global fills the rest, anything still null falls through to the
  CSS defaults in `visuauth.css`. So a tenant resolver can change
  just `Primary` without restating the rest of the palette.
- Wiring: the existing `<va-theme-style />` tag helper becomes
  `ProcessAsync`-based, pulls `ITenantContext.CurrentTenantId`,
  asks the resolver, merges with the global theme, and renders the
  same `:root { ... }` block through `VisuAuthThemeCssRenderer`.
  The output stays identical for single-tenant deployments.
- Sample app: `Sample.WebApp.Theming.SampleTenantThemeResolver`
  maps the seeded tenant ids (`acme`, `globex`, `initech`) to
  distinct presets so flipping the sidebar tenant switcher
  re-skins the dashboard live.

**Out of scope** (deferred):

- Caching — resolvers run on every render; consumers that hit a
  database wrap their own `IMemoryCache` for now. A built-in
  `CachedTenantThemeResolver` decorator can land later if usage
  shows it's needed.
- Per-tenant CSS files (a "drop a `.css` per tenant" model).
  Strict palette overrides cover the 80% case.

---

## Next up (ordered)

With layer 4 in flight, every theming layer named in CLAUDE.md §8.4
is implemented. The natural next milestone is cutting the **0.1**
release — see CLAUDE.md §13. No specific branch is queued here yet
because the release prep (changelog, tag, NuGet push gate) is owner
work, not a standard feature PR.

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
