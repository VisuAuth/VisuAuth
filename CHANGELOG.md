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

- **Microsoft Entra External ID End-user OIDC sign-in**
  (`VisuAuth.EntraExternal.Web` — new NuGet package). Opt-in sub-package
  that closes the customer sign-in loop for the EntraExternal adapter:
  consumers who want their End-user `/visuauth/login` page to render a
  working "Sign in with Microsoft" button add this package on top of
  `VisuAuth.EntraExternal` and call `AddVisuAuthEntraExternalSignIn(...)`.
  Wraps `Microsoft.Identity.Web` against the External authority shape
  (`{tenant}.ciamlogin.com`), registers OIDC + Cookies handlers under
  a stable scheme name, and replaces the no-op `IExternalLoginFlow`
  stub from `VisuAuth.EntraCore` with a real implementation that
  verifies the OIDC-authenticated principal against Microsoft Graph
  via `IUserStore.GetAsync` and returns the `ExternalSignInResult`
  envelope the existing EndUserUi `Callback` page consumes.
  - **`EntraExternalWebOptions`** — TenantSubdomain / TenantId /
    ClientId (required) + ClientSecret / CallbackPath /
    SignedOutCallbackPath / SignInUserFlow (optional). Bound from
    `VisuAuth:EntraExternal:Web` by default. Distinct from the admin
    Graph section because admin (app-only) and end-user (OIDC) flows
    use different app registrations in typical deployments.
  - **`EntraExternalLoginFlow`** — `IExternalLoginFlow`
    implementation. `GetProvidersAsync` surfaces a single
    "Sign in with Microsoft" entry under the scheme name the DI
    extension registers OIDC under. `CompleteSignInAsync` reads the
    OIDC `oid` claim (both long URI and short alias), verifies the
    user exists in Graph, returns `Success` with the directory id —
    or graceful failures with actionable hints when the user is
    missing / the token lacks `oid`. Confirmation strategies fail
    deliberately because the External adapter can't create users
    without Microsoft's hosted user-flow context. `GetPendingInfoAsync`
    falls back from `email` to `preferred_username` so the confirm
    page still renders something meaningful when the token shape
    varies.
  - **DI**: `AddVisuAuthEntraExternalSignIn(IConfiguration ...)` and
    a lambda overload, both binding the new options section, wiring
    `Microsoft.Identity.Web`'s `AddMicrosoftIdentityWebApp` with
    External-appropriate authority + sign-in scheme defaults, and
    using `services.Replace` (not `TryAdd`) to swap the no-op
    `IExternalLoginFlow` stub for the real one — `TryAdd` would
    silently leave the stub in place and the button would never
    render.
  - **Capability overlay**: the flow's `Capabilities` property
    overlays `SupportsExternalProviders = true` on top of the
    `EntraExternalCapabilities` singleton — once OIDC is wired we DO
    have a provider to surface, so any UI that consults the flow's
    capability bag gets the right answer without the CRUD-only
    package's singleton having to lie.
  - **Sample**: `samples/Sample.EntraExternalWebApp/Program.cs` now
    calls `AddVisuAuthEntraExternalSignIn(builder.Configuration)`
    alongside the existing admin wiring. `appsettings.json` documents
    both configuration sections side-by-side. Distinct `UserSecretsId`
    so the new `Web:*` secrets stay isolated.
  - **Tests**: 3 new files (~29 unit tests) covering options
    validation (required `TenantSubdomain` / `TenantId` / `ClientId`
    + computed authority URL), the flow's full branch surface (no
    session / missing oid claim / happy path / AutoCreate vs Confirm
    strategies / pending info fallbacks), and DI registration
    semantics (lambda + IConfiguration overloads, `Replace` vs
    `TryAdd`, OIDC scheme registration, post-configure pinning of
    `SignInScheme` + `NameClaimType`). Suite: 569 unit (was 540, +29)
    + 179 integration = 748 green.
  - **Docs**: `src/VisuAuth.EntraExternal.Web/README.md` with the
    two-app-registration rationale, an end-to-end flow walkthrough,
    and the first-time-strategy table for External (`AutoCreate`
    recommended; `Confirm` strategies graceful-fail with a clear
    hint).
  - **Scope of PR C**: OIDC sign-in foundation. PR D (queued) adds
    user-flow selection, the admin UI to pick which flow the button
    invokes, and attribute mapping from sign-up flows into Graph
    user properties.
  - **Transitive package version bumps** required by
    `Microsoft.Identity.Web` 4.10:
    `Microsoft.Extensions.Logging.Abstractions` and
    `Microsoft.Extensions.DependencyInjection.Abstractions`
    10.0.0 → 10.0.7 (patch-level servicing),
    `System.IdentityModel.Tokens.Jwt` 8.14.0 → 8.18.0 (servicing),
    `Azure.Identity` 1.13.1 → 1.17.2 (patch on 1.x).

- **Microsoft Entra External ID adapter** (`VisuAuth.EntraExternal` — new
  NuGet package). Customer-facing (CIAM / B2C-successor) sibling to
  `VisuAuth.Entra`. Same admin surface, same `IUserStore` /
  `IRoleStore` / `IAuthenticationFlow` contracts; differs in the user
  shape it persists and reads. Activates against any Entra External
  tenant via app-only (client-credentials) Microsoft Graph auth, shared
  with the Workforce adapter through the `VisuAuth.EntraCore` package.
  - **`EntraExternalOptions`** — TenantId / ClientId / ClientSecret /
    `TenantDomain` (the new External-specific required field — used as
    the `issuer` when minting `identities[]` entries) / AppRoleResourceId
    / GraphBaseUrl / DefaultEmailDomain. ValidatesDataAnnotations so a
    missing TenantDomain fails fast at startup, not at the first
    `POST /users` call.
  - **`EntraExternalUserStore`** — full IUserStore surface mirroring
    the Workforce store. The CreateAsync path is the headline
    divergence: identity travels in `identities[]` (signInType =
    `emailAddress`, issuer = `TenantDomain`, issuerAssignedId = the
    customer's email) rather than `UserPrincipalName` (which Microsoft
    auto-generates as `cpim_{guid}@…`). Read paths prefer the customer-
    typed email from identities over the cpim UPN so the admin grid
    stays readable. UpdateAsync deliberately leaves identities / UPN
    / mail untouched — rewriting any of those from a generic admin
    form would lock the customer out of their own login (a footgun
    we made unreachable).
  - **`EntraExternalRoleStore`** — app roles via Graph, identical
    contract to the Workforce role store (the Graph API for app roles
    is tenant-family-agnostic). Deliberately duplicated rather than
    extracted into a shared base — see the type's XML doc for the
    reasoning (avoiding an inverted dependency from EntraCore onto a
    typed options class).
  - **`EntraExternalAuthenticationFlow`** — `SupportsLocalLogin = false`
    shim. Sign-in returns `RedirectToExternalProvider` (the SignIn
    mapper turns that into the "Sign in with Microsoft" hint on
    `/visuauth/login`). Register / reset / confirm return graceful
    failures with the same message; PR-C replaces those with the real
    hosted OIDC redirect via `Microsoft.Identity.Web`.
  - **`EntraExternalUserMapper`** — pure projections. Builds the
    identities-aware Create payload, the identities-fallback read
    projection (identities[emailAddress] → mail → UPN), the safe
    PATCH body (display name + phones only), and an identities-aware
    search filter (`identities/any(id:id/issuerAssignedId eq '…')`)
    so customer-typed emails are findable even when the auto-generated
    UPN doesn't match.
  - **DI**: `services.AddVisuAuthEntraExternal(...)` (both lambda and
    `IConfiguration` overloads). All registrations are `TryAdd` so
    consumers can pre-register their own `IUserStore` / `IAuthenticationFlow`
    test doubles. Reuses the `VisuAuth.EntraCore` no-op stubs for
    `IAuditWriter` / `IJwtIssuer` / `ITenantContext` / `IExternalLoginFlow`
    so an External-only deployment resolves cleanly without the
    Identity adapter wired alongside.
  - **Sample**: new `samples/Sample.EntraExternalWebApp` — ~30-line
    `Program.cs` parallel to `samples/Sample.EntraWebApp` for easy
    A/B comparison between Workforce and External flows. Listens on
    `http://localhost:5260`, distinct `UserSecretsId` so dev
    credentials don't leak between the two adapter families.
  - **Tests**: 6 new unit-test files (~93 tests) covering
    Capabilities, Options validation (including the new required
    `TenantDomain`), the mapper's identities-aware Create + read
    fallback chain + identities-aware search predicate, store
    synchronous defences (null-arg / blank-id / NotSupported branches),
    role store NotSupported branches, and the DI extension's both
    overloads + TryAdd semantics. Suite: 510 unit (was 417, +93) +
    179 integration = 689 green.
  - **Docs**: adapter-specific `src/VisuAuth.EntraExternal/README.md`
    with a tenant-setup walkthrough mirroring the Workforce one (the
    External setup is actually free, unlike Workforce after Microsoft's
    2024 policy change — so this is the lower-friction "try
    VisuAuth against real Graph" path for new contributors).
  - **Scope of PR B**: admin CRUD + sample wiring + capability
    surface. PR C is queued separately and adds the customer-facing
    OIDC redirect via `Microsoft.Identity.Web` so `/visuauth/login`
    becomes a working "Sign in with Microsoft" button instead of just
    a hint.

### Changed

- **Sample.WebApp now uses EF Core migrations** instead of
  `Database.EnsureCreated()`. New `Data/Migrations/` folder with the
  initial migration covers every Identity + VisuAuth table (10 total).
  `UserSeeder.SeedAsync` now calls `Database.MigrateAsync()` on boot
  — schema additions land automatically without the owner having to
  delete `visuauth-sample.db*` by hand. `Microsoft.EntityFrameworkCore.Design`
  added to the Sample csproj as a `PrivateAssets="all"` reference so
  `dotnet ef migrations add` works locally. Existing local DB files
  created by the old `EnsureCreated()` path are missing the
  `__EFMigrationsHistory` table — delete them once and let the
  migration recreate the schema (the `.gitignore` already excludes
  `*.db` so this only affects per-machine dev state).
- **`PLAN.md`** moved the four v0.2 PRs (#30 TOTP, #31 External
  providers, #32 Audit log, #33 Dashboard, #34 Entra adapter) from
  "In flight" to "Recently shipped", refreshed the test counts (now
  579 across the suite), and seeded a v0.3 backlog section
  (EntraExternal adapter, DB-backed adapter config UI, ResetTwoFactor
  for Entra, IAuditReader wrapper for Entra audit logs, cursor-based
  pagination, multi-domain dropdown).

### Added

- **Microsoft Entra ID adapter** (`VisuAuth.Entra` — new NuGet package).
  Implements `IUserStore`, `IRoleStore`, and `IAuthenticationFlow`
  against Microsoft Graph using the app-only (client-credentials)
  auth flow. Activates the capability-flag system end-to-end: by
  declaring `SupportsLocalLogin = false`, the existing end-user UI
  swaps the email/password form for "Sign in with Microsoft" without
  any code change in the consumer app — CLAUDE.md §1.2 + §6.
  - **`EntraOptions`** — TenantId / ClientId / ClientSecret /
    AppRoleResourceId / GraphBaseUrl, bound from configuration or a
    lambda via `services.AddVisuAuthEntra(...)`. ValidatesDataAnnotations
    so a missing tenant id fails fast at startup, not at request time.
  - **`EntraUserStore`** — full IUserStore surface (List / Get /
    GetDetail / Create / Update / SetEnabled / Delete /
    RevokeSessions / ResetPassword). Capability-driven: 2FA reset
    throws NotSupported (per-method DELETE needs typed builders per
    auth-method subtype; scoped to v0.3). Pagination uses Graph's
    page-size and treats every list call as "page 1" — the abstraction's
    1-based page index doesn't map to Graph cursors without state, so
    the v0.2 admin UI relies on filter / search to refine instead of
    walking pages. Pre-flight constraint documented inline.
  - **`EntraRoleStore`** — Graph app-roles. List + Get +
    GetRolesForUser + AssignRole + RemoveRole work; Create / Rename /
    Delete throw NotSupported because app roles are declared in the
    application manifest, not at runtime. Member counts come from a
    single `appRoleAssignedTo` call on the service principal — no
    per-role round-trip.
  - **`EntraAuthenticationFlow`** — capability shim. Every method
    returns either `SignInOutcome.RedirectToExternalProvider` or
    `UserResult.Failure(...)`, because the entire end-user flow is
    hosted by Microsoft. The login page interprets the redirect
    outcome as "show the Microsoft button" via the existing
    SignInPageResponseMapper.
  - **`EntraCapabilities`** — single source of truth for the flag
    declarations both stores read from. Documented per-flag rationale
    (why local login is off, why 2FA reset is deferred, why external
    providers are off because Entra IS the IdP, etc.).
  - **`EntraTemporaryPassword`** — CSPRNG-backed 12-char password
    generator independent of `TemporaryPasswordGenerator` in
    VisuAuth.Identity (so the Entra adapter doesn't acquire a
    dependency on the Identity adapter — CLAUDE.md §2.5). Mixed
    alphabet + class-quota guarantees default Entra password policy
    satisfaction; ambiguous chars (0/O/I/l/1) excluded for read-aloud.
  - **`Sample.WebApp` toggle** — set `VisuAuth:Backend=entra`
    (env var `VISUAUTH_BACKEND=entra`) to flip the entire admin from
    the local Identity backend to the Entra adapter. Same admin UI,
    different IUserStore underneath. `appsettings.json` ships the
    `VisuAuth:Entra:*` placeholder block + a `_VisuAuth` description
    comment; consumers populate via `dotnet user-secrets set
    VisuAuth:Entra:ClientSecret ...` and friends. The Identity wire-up
    is hoisted into a local `WireIdentityBackend` function so the
    Entra branch skips the SQLite DbContext, AddIdentity, JWT issuer,
    external-OAuth wiring, audit-log plugin, and user seeder cleanly.
  - **35 unit tests** cover the pure surface (mapper, capabilities,
    auth-flow shim, temporary-password generator, DI extension
    registrations + TryAdd preservation). Store implementations
    against a live Graph SDK are validated by manual smoke against a
    real tenant — automated integration coverage is gated for v0.3
    when we have a recorded-response harness.
  - **Dependencies added** (scoped to `VisuAuth.Entra` only — the
    other VisuAuth packages remain Graph-free): Microsoft.Graph 5.95.0,
    Azure.Identity 1.13.1, Microsoft.Extensions.Options.DataAnnotations
    10.0.0. Microsoft.Kiota.Abstractions pinned to 1.22.0 to override
    the vulnerable 1.17.1 that Graph 5.95 pulls transitively
    (GHSA-7j59-v9qr-6fq9).

- **Admin dashboard** at `/visuauth/admin` (`VisuAuth.AdminUi`) — the new
  landing page when an admin opens the back office. Replaces the old
  behaviour of "/admin" 404-ing until the user navigated to a sub-route.
  - KPI tiles: Total users, Locked, Pending email confirmation,
    With 2FA, Roles, Tenants. Each tile is clickable and drills into
    the matching list view already filtered (e.g. Locked →
    `/admin/users?isLockedOut=true`). Tiles are capability-aware: a
    backend that reports `SupportsLockout = false` (future Entra
    adapter) doesn't render the Locked tile at all, instead of showing
    a meaningless "0 locked".
  - 7-day login bar chart (UTC days), zero-filled for days with no
    activity so the chart's vertical rhythm stays stable. Pure CSS bars
    — no chart library — driven by a `--bar-height` custom property the
    page model writes per `<li>`.
  - "System health" card surfaces VisuAuth assembly version
    (InformationalVersion → semver incl. pre-release suffix), .NET
    runtime version (`RuntimeInformation.FrameworkDescription`), audit-
    plugin enabled/disabled pill, multi-tenancy enabled/disabled pill.
  - "Recent activity" feed shows the 10 most recent audit events (when
    the plugin is wired); each row's target label links to
    `/admin/audit-log?targetId=…` so an operator can pivot from the
    summary view to the full filtered log in one click. When the plugin
    is off, the card renders an inline "audit log plugin not enabled"
    hint with a link to the audit-log page (which carries the wiring
    snippet).
  - Sidebar gains a "Dashboard" entry as the first item, active when
    the URL is exactly `/visuauth/admin` (StartsWith would have marked
    it active on every admin sub-route).
  - Counts piggy-back on the existing `IUserStore.ListAsync` /
    `IRoleStore.ListAsync` / `ITenantStore.ListAsync` paths
    (`PageSize=1` and read `Total`) so no new abstraction lands — the
    adapter surface stays the same.
  - New `IAuditReader.CountByDayAsync(action, from, to, ct)` returning
    `IReadOnlyList<DailyActionCount>` powers the bar chart without
    pulling every login row into memory. Implementation in
    `EfCoreAuditStore` groups by `DateOnly.FromDateTime(Timestamp.UtcDateTime)`
    in memory after a narrow `Where` push-down — works on every EF
    provider (SQLite included).
  - EN + PT-BR resources for every dashboard label (`Dashboard.*` keys).
  - Sample wires nothing new — the existing audit-log + Identity setup
    light up the dashboard automatically; the home page now links to
    `/visuauth/admin` so the dashboard is the first thing a fresh
    sample-app browser visit lands on after Identity sign-in.

- **Audit log plugin** (`feat/audit-log`) — opt-in trail of every
  sensitive admin and end-user action, surfaced at
  `/visuauth/admin/audit-log`. Activates by adding
  `builder.Services.AddVisuAuthAuditLog()` to `Program.cs`; until then,
  `NoOpAuditWriter` accepts every call at zero cost so handler code
  doesn't have to check whether the plugin is enabled.
  - `IAuditWriter` / `IAuditReader` / `AuditEvent` /
    `AuditFilter` / `AuditEntryView` in `VisuAuth.Abstractions`. The
    write shape is intentionally small (Action / TargetType / TargetId /
    TargetLabel / Outcome / FailureReason / Payload dict) — every other
    field (actor user id + email, IP, user-agent, tenant id, timestamp)
    is enriched by the writer from ambient state.
  - `AuditActions` registry — 40+ stable PascalCase codes that the admin
    page filters on and the i18n / future analytics can hang off
    (UserLocked, RoleAssignedToUser, ExternalProviderSaved,
    LoginSucceeded, LoginFailed, TwoFactorEnabled, etc).
  - `EfCoreAuditStore` (`VisuAuth.Identity`) implements both Writer and
    Reader against the new `VisuAuthAuditLog` table. JSON-serialises the
    payload, snapshots IP / UA truncated to 512 chars, **never** throws
    to the caller — auditing a side action must not break the primary
    action.
  - `AuditRetentionHostedService` runs once a day via
    `TimeProvider`-driven `BackgroundService`; default 90-day retention
    overridable via `AddVisuAuthAuditLog(opts => opts.RetentionDays = 365)`;
    set 0 to keep forever.
  - Capture wired in 26 page handlers: every admin mutation (users,
    roles, tenants, external providers), every end-user self-service
    surface (login success/failure/locked/2FA required, register, reset
    password, email confirm, 2FA setup/verify/recovery code use/disable,
    external login callback + confirm), and both JWT API endpoints.
    Each emits success and failure events with action-specific payload
    — secrets are never logged.
  - Admin page at `/visuauth/admin/audit-log` with filters (actor email
    search, action dropdown, outcome, date range, deep-linkable
    targetId) and pagination. Renders an "audit plugin not enabled"
    explainer with the wiring snippet when `IAuditReader` isn't in DI.
  - Sample wires the plugin out of the box. New tests (255 unit + 173
    integration) cover the EF store + retention + admin page + handler
    capture end-to-end.
- **Sign-in pipeline refactor** (`src/VisuAuth.EndUserUi/Authentication/`)
  — extracted from the login switch that grew unmanageable once audit
  emission landed. Five collaborators replace the inline branching in
  `LoginModel.OnPostAsync` and `AuthApi.LoginAsync`:
  - `SignInChannel` enum (`Web` / `Api`) tags every audit event so the
    admin log can answer "where did this attempt come from?".
  - `SignInAuditMapper` — pure table mapping `SignInOutcome` to the
    audit triple (action / outcome / failure reason). Returning `null`
    for `RedirectToExternalProvider` lets the emitter skip the write.
  - `SignInAuditEmitter` (scoped service) consults the mapper, builds
    the `AuditEvent` with channel + extra payload merged, and delegates
    to `IAuditWriter` — so adding a new outcome means editing one table.
  - `SignInApiResponseMapper` — pure table mapping non-Success
    outcomes to `IResult` (`423 Locked`, `401`, `403 Forbidden`) for
    the minimal-API channel.
  - `SignInPageResponseMapper` — pure table mapping outcomes to a
    `SignInPageOutcome` decision record (`RenderPage` /
    `RedirectTwoFactor` / `RedirectSuccess`) the Razor page interprets
    with localised error keys.
  - Net effect: `AuthApi.LoginAsync` is now orchestration only (~15
    lines) and `LoginModel.OnPostAsync` no longer mixes audit shape /
    HTTP shape / redirect logic. The two response shapes (HTTP vs
    page) stay separate; the audit shape is shared across channels.
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
