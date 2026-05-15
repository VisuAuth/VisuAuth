# Changelog

All notable changes to **VisuAuth** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project follows [SemVer](https://semver.org). Until `1.0.0`, minor bumps
(`0.x`) may include breaking changes — they will always be called out below.

Each release groups changes across the five sibling packages (`VisuAuth`,
`VisuAuth.Abstractions`, `VisuAuth.Identity`, `VisuAuth.AdminUi`,
`VisuAuth.EndUserUi`) since they ship at a single, shared version.

## [Unreleased]

Working toward [`0.2.0`](#020--planned). `<VersionPrefix>` in
`Directory.Build.props` is now `0.2.0`, so merges to `main` publish as
`0.2.0-alpha.<run_number>` pre-releases until the next stable tag.

## [0.2.0] — Planned

Microsoft Entra ID adapter milestone. Tracks CLAUDE.md §13 row for v0.2.

Working list:

- TOTP pages (`/visuauth/two-factor/setup` and `/visuauth/two-factor/verify`)
  plus recovery-code management.
- External login providers (Google, Microsoft, Apple) on the login page,
  driven by registered authentication schemes so the markup is provider-agnostic.
- Audit log plugin — opt-in package writing to a separate
  `VisuAuthAuditLog` EF Core table, with retention policy and admin filter UI.
- Microsoft Entra ID adapter — new package `VisuAuth.Entra` against
  `IUserStore` / `IRoleStore`, exercising the capability flag system
  (`SupportsLocalLogin = false` swaps the login form for a "Sign in with
  Microsoft" button at runtime).

## [0.1.0] — Admin UI, end-user pages, multi-tenancy, theming, mobile

First feature release. Establishes the public surface for the 0.x line: the
admin dashboard, the end-user authentication pages, the multi-tenancy
primitives, the four theming layers, and the mobile JWT / WebView channel.
Tracks the v0.1 milestone in `CLAUDE.md` §13.

### Added — public surface

- `AddVisuAuth<TUser>()` and `MapVisuAuth()` extensions on the meta-package.
  Two lines in `Program.cs` mount the entire experience.

- Fluent composition root for finer control:
  `services.AddVisuAuth()` returns an `IVisuAuthBuilder` and the chain
  methods `UseAspNetIdentity<TUser>()`,
  `EnableMultiTenant(...)` / `EnableMultiTenant<TDbContext, TUser>(...)`
  (the latter also wires the tenant catalogue store), `AddAdminUi()`,
  and `AddEndUserUi()` let consumers opt into individual surfaces. The
  one-liner `AddVisuAuth<TUser>()` delegates to the same chain
  internally, so both forms produce an equivalent service graph.
- `VisuAuth.Abstractions` contracts shaped for adapter plug-in:
  `IUserStore`, `IRoleStore`, `ITenantStore`, `IAuthenticationFlow`,
  `UserBackendCapabilities` (runtime adaptation flag bag), and the supporting
  DTOs (`UserSummary`, `UserDetail`, `UserClaim`, `ExternalLogin`,
  `UserFilter`, `UserSortBy`, `CreateUserCommand`, `UpdateUserCommand`,
  `UserResult`, `PagedResult<T>`, `RoleSummary`, `TenantSummary`,
  `TenantOptions`, `ITenantContext`).

### Added — ASP.NET Core Identity adapter (`VisuAuth.Identity`)

- `AspNetIdentityUserStore<TUser>` covering the full `IUserStore` surface:
  search + role / status / verified / 2FA filters + pagination, get / detail,
  create, update, delete, lock / unlock, reset password (CSPRNG-generated
  policy-compliant temporary password), reset 2FA, revoke sessions.
- `AspNetIdentityRoleStore<TUser, TRole>` with full `IRoleStore` surface:
  list / get / create / rename / delete / get-roles-for-user / assign / remove.
- `AspNetIdentityTenantStore<TUser>` with full `ITenantStore` surface; member
  counts use `IgnoreQueryFilters()` so the catalogue sees every tenant
  regardless of the current scope.
- Multi-tenancy primitives: `MultiTenantIdentityUser` base, the
  `MultiTenantIdentityDbContext<TUser>` base with an `IMultiTenantEntity`
  global query filter, `TenantSaveChangesInterceptor` that auto-stamps
  `TenantId` on insert, and header + cookie tenant resolver middleware.
- `TemporaryPasswordGenerator` — CSPRNG-backed, honours the configured
  Identity password policy, avoids visually ambiguous characters.
- DI extensions: `AddVisuAuthIdentityAdapter<TUser>()`,
  `AddVisuAuthIdentityAdapter<TUser, TRole>()`,
  `EnableVisuAuthTenancy(opts)`,
  `EnableVisuAuthTenancy<TDbContext, TUser>(opts)`.

### Added — Admin dashboard (`VisuAuth.AdminUi`)

- `/visuauth/admin/users` list page with htmx-powered live search,
  pagination, and role / status / verified / 2FA filters. htmx partials swap
  the table without a full reload.
- `/visuauth/admin/users/{id}` user detail with inline profile edit,
  lock / unlock, reset password (one-time temporary password with a
  click-to-copy widget), reset 2FA, revoke sessions, delete, and
  role assign / remove.
- `/visuauth/admin/users/new` create-user form (autogenerates a temporary
  password when blank, optional role checkboxes).
- `/visuauth/admin/roles` catalogue with member counts and inline
  create / rename / delete.
- `/visuauth/admin/tenants` catalogue with member counts and inline
  create / rename / delete. Sidebar tenant switcher (cookie-backed) scopes
  every admin view when multi-tenancy is on.
- Default CSS theme with custom-property hooks for branding, sidebar active
  state computed from the current route, and a vanilla-JS click-to-copy
  widget for one-time secrets.

### Added — End-user pages (`VisuAuth.EndUserUi`)

- `/visuauth/login` and `/visuauth/logout` (email + password + remember-me,
  anti-forgery, redirect-back to `returnUrl`).
- `/visuauth/register`, `/visuauth/forgot-password`, `/visuauth/reset-password`,
  `/visuauth/confirm-email` Razor Pages.
- Mobile / native API channel at `/visuauth/api/auth/{login,register,refresh}`
  issuing HS256 JWTs (`sub`, `email`, `tenant_id`, `roles`, `exp`).
- WebView deep-link callback: when `returnUrl` parses to an allow-listed
  non-HTTP scheme, login redirects to it with the JWT in the URL fragment.
  Opt-in preview page for desktop developer testing.
- Shared `<va-password>` and `<va-form-errors>` tag helpers, plus a
  `visuauth.js` show / hide password widget.
- Layout reuses the admin CSS for visual continuity.

### Added — Theming (CLAUDE.md §8.4)

- **Layer 1 — CSS custom properties.** Consumers override `--visuauth-*`
  variables in their own stylesheet loaded after ours.
- **Layer 2 — programmatic config.** `services.Configure<VisuAuthTheme>(...)`
  emits CSS variable overrides via the `<va-theme-style />` tag helper. The
  sample app ships preset palettes (`Default`, `Purple`, `Orange`, `Forest`,
  `Midnight`, `Serif`).
- **Layer 3 — view override.** Drop a same-named `.cshtml` in
  `/Views/VisuAuth/` (root configurable via `VisuAuthViewOverrideOptions`)
  and Razor uses it instead of the package default. Covers partials and
  layouts via `IViewLocationExpander`, and full Razor Pages via an
  `IPageRouteModelConvention` that demotes our routes so a consumer page at
  the same `@page` route wins.
- **Layer 4a — per-tenant theme.** Consumers implement
  `ITenantThemeResolver` and VisuAuth overlays the per-tenant
  `VisuAuthTheme` on top of the global one via `VisuAuthThemeMerger`.
  Default registration is a no-op so single-tenant deployments keep the
  layer-2 fast path.
- **Layer 4b — per-tenant view overrides.** Consumers implement
  `ITenantViewOverrideResolver` to map the current tenant id to an
  override root; the view-location expander prepends it ahead of the
  global override paths. Tenant id is baked into Razor's view-location
  cache key so tenant A's swapped template never shadows tenant B's.

### Added — i18n

- JSON-backed `IStringLocalizer<AdminSharedResources>` and
  `IStringLocalizer<EndUserSharedResources>` (provider:
  `My.Extensions.Localization.Json`), with `en` and `pt-BR` translations.
- Culture resolution via query, cookie, or header, plus a
  `POST /visuauth/culture` switch endpoint with an open-redirect guard and
  the `<va-language-switcher />` tag helper in both layouts.

### Added — assets and air-gapped support

- htmx 2.0.4 shipped as an embedded static asset
  (`VisuAuth.AdminUi/wwwroot/htmx.min.js`); both layouts reference
  `/_content/VisuAuth.AdminUi/htmx.min.js` so air-gapped deployments work
  without an outbound CDN call.

### Added — sample app and tests

- `samples/Sample.WebApp` — ASP.NET Core Identity + EF Core SQLite, 12
  seeded users + 3 seeded tenants, drop-in usage proven (`AddVisuAuth` +
  `MapVisuAuth`).
- `tests/VisuAuth.UnitTests` (xUnit + FluentAssertions + Moq) and
  `tests/VisuAuth.IntegrationTests` (`WebApplicationFactory<Program>`),
  with shared `Method_Scenario_ExpectedResult` naming.

### Infrastructure

- Apache 2.0 license, NuGet meta-package + four sibling packages.
- GitHub Actions: CI workflow (build + test + pack on PR) and Release
  workflow (NuGet publish — pre-release on every merge to `main`, stable on
  a `vX.Y.Z` tag push).
- SonarCloud analysis on CI plus a local `scripts/sonar-local.ps1` runner
  against a Docker Compose SonarQube stack.
- Central Package Management via `Directory.Packages.props`.

### Notes for consumers

- This is the first feature release. Pre-1.0, breaking changes can land on
  any `0.x` bump; they will be flagged here.
- The Microsoft Entra ID adapter, TOTP pages, and external login providers
  are scheduled for `0.2` — see `CLAUDE.md` §13.

## [0.0.1-alpha] — Name reservation

Placeholder release on NuGet to reserve the `VisuAuth` package name. No
runtime code; pin to `0.1.0+` for real features.

[Unreleased]: https://github.com/VisuAuth/visuauth/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/VisuAuth/visuauth/releases/tag/v0.1.0
[0.0.1-alpha]: https://github.com/VisuAuth/visuauth/releases/tag/v0.0.1-alpha
