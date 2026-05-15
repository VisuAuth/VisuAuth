# PLAN.md — VisuAuth

Living document. Tracks current state, the active milestone backlog, and the
immediate next step. Updated as PRs land. Long-term direction lives in
`CLAUDE.md` section 13 (Roadmap).

---

## Current status

- **Version in development**: v0.2 (Microsoft Entra ID adapter milestone)
- **Latest shipped on NuGet**: [`VisuAuth 0.1.0`](https://www.nuget.org/packages/VisuAuth/0.1.0) — first feature release (admin UI, end-user pages, multi-tenancy, four theming layers, mobile JWT + WebView)
- **Default branch**: `main` at <https://github.com/VisuAuth/visuauth>
- **Build state**: green (`dotnet build src/VisuAuth.slnx -c Release` → 0 errors, 0 warnings)
- **Test state**: green on `main` (114 unit + 133 integration = 247 tests)

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
  - Per-tenant theme (theming layer 4a) — consumers implement `ITenantThemeResolver` and VisuAuth overlays the per-tenant `VisuAuthTheme` on top of the global one via `VisuAuthThemeMerger`; default registration is a no-op so single-tenant deployments keep the layer-2 fast path
  - Per-tenant view overrides (theming layer 4b) — consumers implement `ITenantViewOverrideResolver` to map the current tenant id to an override root; the view-location expander prepends it ahead of the global override paths, and the resolved tenant id is part of Razor's view-location cache key so tenant A's swapped template never shadows tenant B's. Default `NoOpTenantViewOverrideResolver` returns `null` so single-tenant deployments pay nothing
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

Nothing in flight. v0.1 shipped to NuGet on the `v0.1.0` tag and the
GitHub Release is published. `<VersionPrefix>` is now `0.2.0` so the
next merge to `main` will emit a `0.2.0-alpha.<run_number>` pre-release.

---

## Next up — v0.2: Microsoft Entra ID adapter milestone

CLAUDE.md §13 names four items for v0.2:

1. **Microsoft Entra ID adapter** — admin UI against the Microsoft Graph
   API. `IUserStore` / `IRoleStore` adapter declares
   `SupportsLocalLogin = false`; the end-user UI swaps the email/password
   form for a "Sign in with Microsoft" button automatically (CLAUDE.md
   §6 capability-driven UI). New package `VisuAuth.Entra` referencing
   only `VisuAuth.Abstractions`. Must NOT leak into `VisuAuth.Identity`.
2. **TOTP pages** — `/visuauth/two-factor/setup`,
   `/visuauth/two-factor/verify`, recovery-code management. Lives in
   `VisuAuth.EndUserUi`; depends on the Identity `UserManager` 2FA
   APIs that already ship.
3. **External login providers** — Google, Microsoft, Apple buttons on
   the login page. Shaped via `IAuthenticationFlow.ExternalProviders`
   so the UI iterates the registered schemes (no per-provider markup).
4. **Audit log plugin** — opt-in package writing to a separate
   `VisuAuthAuditLog` EF Core table (CLAUDE.md §2.5 "Optional
   VisuAuth-specific tables…are explicit and documented"). Retention
   policy + filter UI in admin.

No branches queued yet — each item lands as its own feature branch +
PR per CLAUDE.md §11. Owner picks the order; the natural sequencing is
**TOTP → external providers → audit log → Entra ID adapter** because
the first three exercise the existing abstractions and the fourth is
the big "does the capability flag system actually work" stress test.

---

## Recently shipped

### v0.1.0 (tag `v0.1.0`, on NuGet)

Owner cut the release at commit `3554d09`. CI pushed five nupkgs
(`VisuAuth`, `VisuAuth.Abstractions`, `VisuAuth.Identity`,
`VisuAuth.AdminUi`, `VisuAuth.EndUserUi`) to nuget.org. GitHub Release
published with the highlights body. See `CHANGELOG.md` `[0.1.0]` for
the full list.

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
