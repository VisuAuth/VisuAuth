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

### Added

- TOTP pages for self-service two-factor authentication
  (`VisuAuth.EndUserUi`):
  - `/visuauth/two-factor/setup` — pair an authenticator app via inline
    SVG QR code (rendered with QRCoder) plus manual-entry shared key,
    confirm the first 6-digit code to enable.
  - `/visuauth/two-factor/verify` — post-password challenge accepting either
    a TOTP code or a one-shot recovery code, with optional "trust this
    device" checkbox that flips the persistent flag on the cookie.
  - `/visuauth/two-factor/recovery-codes` — generate / regenerate the
    10-code batch (each individually copyable) and disable 2FA from one
    place.
  - `/visuauth/login` redirects to the verify page automatically when the
    Identity adapter returns `RequiresTwoFactor`, forwarding the original
    `returnUrl` and remember-me preference.
  - End-user layout grows a "Setup 2FA" / "Sign out" header link when the
    visitor is signed in.
- `ITwoFactorFlow` abstraction in `VisuAuth.Abstractions` so non-Identity
  adapters (Entra) can opt out via
  `UserBackendCapabilities.SupportsTwoFactor` while the UI surface stays
  identical.
- `AspNetIdentityTwoFactorFlow<TUser>` in `VisuAuth.Identity` wiring the
  ASP.NET Identity `UserManager` 2FA APIs (authenticator key, recovery
  codes, TOTP / recovery sign-in) to the new abstraction.
- `OtpAuthUriBuilder` helper in `VisuAuth.Abstractions` for the canonical
  RFC 6238 / Key Uri Format `otpauth://totp/...` URI.
- Sample app pre-enrols a `twofactor.demo@example.com` account with a
  deterministic shared key so the challenge flow is reachable without
  pairing first; pairing details + the user are linked from `/`.
- TOTP setup page exposes the encoded `otpauth://` URI as a
  `data-otpauth-uri` attribute on the QR container — useful for desktop
  developers who want to copy the URI directly into a password manager
  or import it elsewhere without scanning.
- External-login provider buttons on `/visuauth/login`
  (`VisuAuth.EndUserUi`):
  - One "Continue with {provider}" button per scheme registered via
    ASP.NET Core's authentication pipeline (Google, Microsoft, Apple,
    GitHub — anything that ships an OAuth handler). Renders below the
    password form with an "or" divider; auto-suppresses when no provider
    is wired so consumers who never call `AddGoogle()` / etc. see no
    visual change.
  - `/visuauth/external-login/start` POST-only kickoff that builds the
    `ChallengeResult` for the selected scheme; rejects unknown schemes
    silently to prevent provider probing.
  - `/visuauth/external-login/callback` lands the OAuth redirect, applies
    the configured first-time strategy, and signs the user in. Mirrors
    the WebView deep-link path from `/visuauth/login`, so an external
    sign-in with an allow-listed `returnUrl` mints a JWT and redirects
    via the mobile fragment.
  - `/visuauth/external-login/confirm` collects email + optional username
    when the strategy needs explicit consent before account creation.
- `ITwoFactorFlow`-style `IExternalLoginFlow` abstraction in
  `VisuAuth.Abstractions` with `GetProvidersAsync` / `CompleteSignInAsync`
  / `ConfirmAndCreateAsync`, plus DTOs (`ExternalProviderInfo`,
  `ExternalSignInResult`, `ExternalLoginFirstTimeStrategy`).
- `ExternalLoginOptions.FirstTimeStrategy` lets consumers pick between
  three behaviours when an external identity has no linked local user:
  - `AutoCreate` (default) — provisions a local user from the provider's
    claims and signs in.
  - `AutoLinkByEmailOrConfirm` — auto-links to an existing local user
    when the provider's email matches; otherwise routes to `/confirm`.
  - `AlwaysConfirm` — always shows `/confirm` regardless of email match.
- `AspNetIdentityExternalLoginFlow<TUser>` in `VisuAuth.Identity`
  implements all three strategies on top of the existing
  `SignInManager<TUser>` external-login surface.
- Sample app wires Microsoft conditionally — only when
  `Microsoft:ClientId` + `Microsoft:ClientSecret` are present in
  configuration (typically via `dotnet user-secrets`). The home page
  documents the app-registration steps + which redirect URI to register.
- **Admin UI for external providers** at `/visuauth/admin/external-providers`
  (`VisuAuth.AdminUi`): lists every pre-registered OAuth scheme with its
  `ClientId` / secret state / enabled flag, supports inline edit + per-row
  toggle + bulk enable-all / disable-all. Edits land in the new
  `VisuAuthExternalProviderConfigs` table; the `IOptionsMonitorCache` for
  the matching scheme is invalidated on save so the next sign-in attempt
  picks up the fresh credentials **without an app restart**.
- `IExternalProviderConfigStore` abstraction (`VisuAuth.Abstractions`)
  with `ExternalProviderConfigView` (UI-safe — never returns the
  plaintext secret) and `SaveExternalProviderConfigCommand` (lets the
  admin edit `ClientId`/`IsEnabled` without re-typing the secret via the
  "PlainTextClientSecret = null preserves existing ciphertext" rule).
- `EfCoreExternalProviderConfigStore` (`VisuAuth.Identity`) encrypts the
  `ClientSecret` at rest via ASP.NET Core's `IDataProtectionProvider` —
  ciphertext stays in the DB, plaintext is only ever decrypted server-side
  for the auth options configurator (never flushed to admin UI responses).
- `DynamicExternalProviderOptionsConfigurator<TOptions>` overlays
  admin-edited credentials on top of the consumer's static
  `AddXxx(o => ...)` registration. Wired per scheme via
  `services.AddVisuAuthDynamicExternalProviderOptions<MicrosoftAccountOptions>("Microsoft")`
  — backward-compatible: schemes the consumer never opts in keep using
  their static options.
- `IExternalProviderOptionsCacheInvalidator` (`VisuAuth.Abstractions`)
  evicts the `IOptionsMonitorCache` entry for a scheme so the next auth
  challenge rebuilds options from the freshly-saved DB values — the
  no-restart story end-to-end.
- `AspNetIdentityExternalLoginFlow.GetProvidersAsync` now intersects the
  registered auth schemes with the store's enabled-and-populated rows.
  Consumers without the store keep "all registered schemes" behaviour
  (the constructor parameter is optional).
- Provider buttons on `/visuauth/login` render the brand SVG icon next
  to the label via a new `<va-provider-icon scheme="…" />` tag helper
  (`VisuAuth.AdminUi`). Microsoft / Google / Apple / GitHub get their
  official brand mark; any other scheme falls back to a generic key glyph.
- Per-tenant schema column already in place (`TenantId` on
  `VisuAuthExternalProviderConfig` + unique index on `(Scheme, TenantId)`)
  so the Phase 1.5 per-tenant runtime can land as a non-breaking change.
- Sample app pre-registers all four schemes (Microsoft / Google / GitHub /
  Apple) with placeholder-or-appsettings defaults so the admin UI can
  fully configure a provider from scratch via the browser — no code
  change required. `UserSeeder` reads the new `IExternalProviderRegistry`
  to seed a row for every wired scheme on boot, so adding a fifth
  provider in `Program.cs` no longer needs a matching seeder edit.
- **External provider discoverability** in the admin UI:
  - `IExternalProviderRegistry` (`VisuAuth.Abstractions`) — singleton
    populated by every `AddVisuAuthDynamicExternalProviderOptions<TOptions>`
    call. The page consults this as the source of truth for "what's
    actually wired and runnable", not the DB.
  - Built-in catalogue of ~20 popular OAuth providers
    (`KnownProviderCatalog` in `VisuAuth.AdminUi`): Microsoft, Google,
    Apple, Facebook, GitHub, GitLab, Reddit, LinkedIn, X / Twitter,
    Discord, Slack, Twitch, Spotify, Amazon, Salesforce, Notion, PayPal,
    Patreon, Zoom, Shopify. Each entry carries scheme, display name,
    category, NuGet package id, options-type name, fluent extension method,
    and a docs URL.
  - Page renders four buckets via an OUTER JOIN of registry + catalogue +
    DB: **Active** (wired + known, editable), **Custom** (wired but
    outside the catalogue, editable with a "custom" badge), **Available**
    (catalogue entries the host hasn't wired — ghost cards with a
    copy-pasteable wiring snippet under a "How to activate" disclosure),
    and **Orphaned credentials** (DB rows for schemes no longer wired —
    warning section with `Delete` button).
  - `IExternalProviderConfigStore.DeleteAsync` (idempotent) backs the
    orphan-row cleanup path.
  - Brand SVG icons added for all 16 new catalogue entries — same inline,
    no-CDN approach as the existing Microsoft / Google / Apple / GitHub
    glyphs.
  - Save handler now rejects attempts to write into the DB for schemes
    the host didn't wire (would silently dead-end — no handler means no
    login button), surfacing a clear localized error instead.
  - **Source badges** on every Client ID / Client Secret cell: a
    "from DB" pill (database glyph) appears when the value comes from
    `VisuAuthExternalProviderConfigs`, a "from code" pill (code-chevrons
    glyph) appears when the value comes from `appsettings` /
    `user-secrets` / a `Program.cs` lambda. Both render together when
    both sources have a value, with a tooltip explaining that the DB
    value wins at runtime. Backed by `IExternalProviderStaticConfigSnapshot`
    (`VisuAuth.Abstractions`) — a singleton populated by the dynamic
    options configurator just before its overlay runs, so the snapshot
    detects static values from *any* consumer convention without
    hard-coding a configuration-key pattern.

### Fixed (in-flight before first 0.2 pre-release)

- TOTP setup QR now carries a `viewBox` so CSS scaling renders as true
  vector graphics — the previous `width`/`height`-only SVG was scaled
  bitmap-style by browsers, producing sub-pixel module edges that
  authenticator camera apps refused to lock on to. Manual key entry was
  unaffected; only QR scanning was failing.
- Setup page now renders the verification-code error inline next to the
  input (with an autoscroll fragment) instead of at the top of the
  page, where it was scrolled out of view behind the QR + manual key.
- Setup page error text is now properly localized through
  `IStringLocalizer<EndUserSharedResources>` instead of leaking the
  English fallback string from the Identity adapter.
- `[Authorize]`'d end-user pages (e.g. TOTP setup) now redirect anonymous
  visitors to `/visuauth/login` — `AddVisuAuthEndUserUi` post-configures
  the Identity cookie's `LoginPath` so the default `/Account/Login` no
  longer 404s in apps that only ship VisuAuth's pages.
- TOTP challenge page (`/visuauth/two-factor/verify`) now splits errors
  by form: an authenticator-code failure renders above the code input
  with the "try the latest code from your app" wording, while a
  recovery-code failure renders inside the recovery `<details>` (which
  auto-reopens) with a recovery-specific "invalid or already used" message.
  The previous shared error message bled the authenticator phrasing into
  recovery failures and rendered above the visible form regardless of
  which one the user submitted.
- Sample app seeder now also pre-enrols a deterministic recovery batch
  (`demo1-aaaaa`, `demo2-bbbbb`, `demo3-ccccc`) on `twofactor.demo@example.com`
  so the recovery-code flow is exercisable from the home page without
  first generating a batch on the recovery-codes page. Codes + a clock-drift
  hint are linked from `/`.

### Dependencies

- Adds [QRCoder 1.6.0](https://github.com/codebude/QRCoder) (MIT) as a
  direct dependency of `VisuAuth.EndUserUi`. Used by the TOTP setup page
  to render the `otpauth://` URI as inline SVG; no other surface uses it.
- Sample-only NuGet additions (the VisuAuth packages still ship zero
  provider deps): `Microsoft.AspNetCore.Authentication.Google`,
  `AspNet.Security.OAuth.Apple`, and `AspNet.Security.OAuth.GitHub` —
  used by `samples/Sample.WebApp` to demonstrate the four pre-registered
  providers + the admin edit story.
- Bumps `System.IdentityModel.Tokens.Jwt` from 8.2.1 to 8.14.0 to match
  the transitive constraint introduced by Apple's OAuth provider.
- Test-only: `Microsoft.EntityFrameworkCore.InMemory` 10.0.0 added to
  `VisuAuth.UnitTests` for the new
  `EfCoreExternalProviderConfigStoreTests` (round-trips through an
  in-memory DbContext instead of standing up SQLite per test).

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
