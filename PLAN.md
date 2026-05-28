# PLAN.md — VisuAuth

Living document. Tracks current state, the active milestone backlog, and the
immediate next step. Updated as PRs land. Long-term direction lives in
`CLAUDE.md` section 13 (Roadmap).

---

## Current status

- **Version in development**: between milestones — v0.2 fully merged to `main`, awaiting a `v0.2.0` tag from the owner to ship; v0.3 backlog forming.
- **Latest shipped on NuGet**: [`VisuAuth 0.1.0`](https://www.nuget.org/packages/VisuAuth/0.1.0) — first feature release. The v0.2 release will publish six packages (the five `0.1.0` ones plus the new `VisuAuth.Entra`).
- **Default branch**: `main` at <https://github.com/VisuAuth/visuauth>
- **Build state**: green (`dotnet build src/VisuAuth.slnx -c Release` → 0 errors, 0 warnings)
- **Test state**: green on `main` (417 unit + 179 integration = 596 tests after the EntraCore extraction; PR B's adapter + sample push the in-flight branch to 510 unit + 179 integration = 689 tests)

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

- **Cursor-based pagination** (`feat/cursor-pagination`) — replaces the
  numeric `PagedResult.Page` / `PageSize` / `TotalPages` contract with a
  forward, opaque `NextCursor` (string) plus an optional `TotalCount`
  (int?). EF stores (`AspNetIdentityUserStore`, `EfCoreAuditStore`) encode
  the next offset as the cursor and still supply a real `COUNT`; the Graph
  user stores (`EntraUserStore`, `EntraExternalUserStore`) now follow
  Graph's `@odata.nextLink` natively (no more "every call is page 1") with
  `TotalCount` null. `EntraAuditReader` stays a single page (no cursor)
  because it merges two independently-tokenised Graph sources client-side.
  `UserFilter` / `AuditFilter` swap `Page` for `Cursor`. A new SSRF-safe
  `GraphPageCursor` (EntraCore) validates a decoded continuation link
  resolves to the configured Graph origin before `WithUrl(...)` is called,
  so a tampered cursor can't redirect a bearer-token request off-host; EF
  stores use an `OffsetCursor` codec that decodes garbage to page 1. Admin
  pagers: "Next" follows the cursor (htmx + `hx-push-url`), "Previous"
  steps back through browser history, and the count line shows the real
  total when known or a per-page count otherwise. Tests: `OffsetCursor` +
  `GraphPageCursor` codecs (round-trip, tamper/SSRF rejection), EF audit
  cursor round-trip, Graph skiptoken round-trip + off-origin rejection.
  **Breaking** on `VisuAuth.Abstractions` — flagged in CHANGELOG.

---

## Next up — v0.3 backlog

Roadmap row from CLAUDE.md §13 is *"Microsoft Entra External ID
adapter, profile / sessions management, bulk operations, view-level
customization"*. The External adapter shipped across PRs A/B/C
(#36 EntraCore extraction, #37 CRUD adapter, #38 OIDC sign-in) plus two
admin-robustness fixes (#39 roles, #40 external-providers) and PR D
(#41 profile sync). Follow-ups #42 (ResetTwoFactor), #43 (sign-in audit
reader), #44 (directoryAudits merge) and #45 (multi-domain create-user
dropdown) also landed. Other concrete ideas surfaced during v0.2:

1. **User-flow management admin UI** *(blocked on Graph v1.0)* — the
   originally-envisioned `/visuauth/admin/entra-external/user-flows`
   page (list / pick / edit sign-up + sign-in flows and their collected
   attributes) needs the `b2cUserFlow` / `authenticationEventsFlow` /
   `userFlowAttribute` resources, which are **beta-only** in Microsoft
   Graph. Parked until either those graduate to v1.0 or we decide to
   take a dependency on `Microsoft.Graph.Beta`. PR D delivered the
   attribute-mapping value (claims → Graph user) without it.
2. **DB-backed adapter config UI** — `/visuauth/admin/entra-config`
   (and similar for future adapters). Pattern reuses the existing
   External Providers infrastructure: `IConfigureOptions<TOptions>`
   overlay on top of `IConfiguration`, source badges ("from DB" /
   "from code"), cache invalidator so saves take effect on the next
   Graph call without restart. Possibly generalised to
   `VisuAuthAdapterConfigs` (section/key/value/isSecret) so future
   adapters (LDAP, Cognito, …) get the same admin surface for free.

No branches queued yet — each item lands as its own feature branch +
PR per CLAUDE.md §11. Owner picks order.

---

## Recently shipped

### v0.2 (merged to `main`, awaiting `v0.2.0` tag)

Five PRs landed on `main` in milestone order. CI publishes pre-release
nupkgs on every merge; cutting the tag flips them to stable.

- **#30 `feat/two-factor-totp`** — TOTP setup / challenge / recovery
  codes (`/visuauth/two-factor/setup,verify,recovery-codes`), inline
  SVG QR via QRCoder, capability-gated via
  `UserBackendCapabilities.SupportsTwoFactor`.
- **#31 `feat/external-login-providers`** — `/visuauth/external-login/*`
  pages with `IExternalLoginFlow` + three first-time strategies, plus
  `/admin/external-providers` admin UI with secret encryption at rest,
  dynamic option overlay, and `KnownProviderCatalog` ghost cards for
  ~20 popular providers.
- **#32 `feat/audit-log`** — opt-in `AddVisuAuthAuditLog()` plugin
  with EF-backed writer + reader + retention service. 26 instrumented
  handler call sites, `/admin/audit-log` filter UI. The login switch
  was refactored into the five-class sign-in pipeline
  (`SignInChannel`, `SignInAuditMapper`, `SignInAuditEmitter`,
  `SignInApiResponseMapper`, `SignInPageResponseMapper`) so both the
  Razor LoginModel and the minimal-API AuthApi orchestrate the same
  shape.
- **#33 `feat/admin-dashboard`** — `/visuauth/admin` landing page
  with KPI tiles, 7-day login bar chart, system-health card, recent
  activity feed. Tiles are capability-aware so an Entra-mode deploy
  hides Locked / 2FA / PendingEmail automatically. Added
  `IAuditReader.CountByDayAsync` for the chart series.
- **#34 `feat/entra-adapter`** — Microsoft Entra ID Workforce adapter
  (`VisuAuth.Entra` new package). `IUserStore` + `IRoleStore` +
  `IAuthenticationFlow` against Microsoft Graph via app-only
  ClientSecretCredential. Capability flags (`SupportsLocalLogin =
  false` + friends) flip the UI automatically; the Login page swaps
  to a Microsoft hint, dashboard hides Lockout/2FA tiles, etc. New
  `EmailDomainSuffix` capability + `EntraOptions.DefaultEmailDomain`
  drive a split input on `/admin/users/new` (locked verified domain
  suffix). Two samples: `Sample.WebApp` with `VISUAUTH_BACKEND=entra`
  toggle, and a minimal `Sample.EntraWebApp` (~30-line Program.cs).
  Adapter-specific README in `src/VisuAuth.Entra/README.md`.
  Validated end-to-end against a real tenant (`visuauth.onmicrosoft.com`
  + `visuauth.com` multi-domain).

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
