# Screenshot / GIF capture checklist

The v1.0 docs are meant to be **image-heavy** — show the UI, don't just
describe it. This checklist tracks every capture the site needs, the page and
state to shoot it in, and the canonical filename under
`docs/assets/screenshots/`. Placeholders (`.svg`) ship first; replace them with
real captures as they are produced.

## Capture conventions

- **Source.** Run [`samples/Sample.WebApp`](https://github.com/VisuAuth/VisuAuth/tree/main/samples/Sample.WebApp) —
  it exercises every surface and has seedable demo data.
- **Viewport.** 1280×720 for full pages; crop tightly for component shots. Keep
  a consistent browser zoom (100%).
- **Demo data.** Use realistic but obviously-fake names/emails (e.g.
  `alice@example.com`). Never capture real personal data.
- **Both themes.** Where a light/dark pair is listed, capture both with the
  same data and viewport so they line up.
- **GIFs.** Keep under ~3 MB; trim to the interaction (no idle frames).
- **Filenames.** Lower-kebab-case, matching the canonical name below. Saving as
  `.png` / `.gif` instead of the placeholder `.svg`? Update the one
  `![…](…)` reference in the listed page.

## Status legend

- [ ] not captured (placeholder in place)
- [x] real capture committed

## Getting started + Home

- [x] `screenshots/admin-dashboard.png` — `/visuauth/admin`, light theme, a few
  seeded users visible. *(Used on `index.md` and as the hero.)*
- [x] `screenshots/enduser-login.png` — `/visuauth/login`, default theme.
  *(Used on `getting-started.md`.)*

## Admin UI

- [x] `screenshots/admin-user-list-search.gif` — the user list + typing in
  search → live (htmx) filtering. Used in place of a static list still.
- [ ] `screenshots/admin-user-detail.png` — a single user's detail page
  (claims, roles, lockout / 2FA state).
- [x] `screenshots/admin-user-create.png` — the create-user form.
- [ ] `screenshots/admin-user-actions.png` — the lock / reset-password /
  force-logout / reset-2FA actions on a user.
- [x] `screenshots/admin-roles.png` — the roles catalogue.
- [x] `screenshots/admin-role-assign.gif` — assigning a role to a user.
- [ ] `screenshots/admin-dashboard-dark.png` — `/visuauth/admin` in dark theme
  (pairs with `admin-dashboard.png`).

## End-user UI

- [ ] `screenshots/enduser-login-dark.png` — `/visuauth/login`, dark theme.
- [x] `screenshots/enduser-register.png` — `/visuauth/register`.
- [ ] `screenshots/enduser-forgot-password.png` — `/visuauth/forgot-password`.
- [ ] `screenshots/enduser-reset-password.png` — `/visuauth/reset-password`.
- [x] `screenshots/enduser-password-toggle.gif` — the show/hide password eye
  toggling.
- [x] `screenshots/enduser-lang-switcher.gif` — toggling pt-BR ⇄ en via the
  flag switcher.

## Multi-tenancy

- [x] `screenshots/tenants-catalogue.png` — the tenant catalogue admin page.
- [x] `screenshots/tenant-switcher.gif` — the admin sidebar tenant switcher
  changing scope.
- [ ] `screenshots/tenant-create.png` — creating a tenant.

## Theming

- [ ] `screenshots/theming-default.png` — default theme baseline (for
  before/after pairs).
- [ ] `screenshots/theming-css-tokens.png` — layer 1: re-branded via CSS
  variables (custom primary color + logo).
- [x] `screenshots/theming-light-dark.gif` — the built-in light/dark toggle.
- [x] `screenshots/theming-per-tenant.png` — layer 4: two tenants rendering
  with different brand colors.

## Two-factor

Wanted by `two-factor.md`, which currently ships text-only.

- [ ] `screenshots/two-factor-setup.png` — `/visuauth/two-factor/setup` with the
  QR code and manual key visible. Use a throwaway secret; never a real one.
- [ ] `screenshots/two-factor-verify.png` — the sign-in challenge page.
- [ ] `screenshots/two-factor-recovery-codes.png` — the recovery-codes screen.
  Redact or use obviously-fake codes.

## External logins

Wanted by `external-logins.md`, which currently ships text-only.

- [ ] `screenshots/external-providers-admin.png` — `/visuauth/admin/external-providers`
  with a couple of providers configured and the "from code" / "from DB" source
  badges visible. **Blank out the client secrets.**
- [ ] `screenshots/enduser-login-providers.png` — the sign-in page showing the
  "or sign in with" provider buttons.
- [ ] `screenshots/external-login-confirm.png` — the first-time confirmation
  page (`AlwaysConfirm` strategy).

## Audit log

Wanted by `audit-log.md`, which currently ships text-only.

- [ ] `screenshots/audit-log.png` — `/visuauth/admin/audit-log` with a mix of
  success and failure rows. Seed the data; don't capture real activity.
- [ ] `screenshots/audit-log-filters.gif` — filtering the log by action / date.

## Mobile & JWT

- [ ] `screenshots/mobile-webview-callback.png` — the WebView callback preview
  page.

## Securing the admin

- [ ] `screenshots/admin-access-denied.png` — what a signed-in non-admin sees
  when the dashboard is restricted with `RequireRole(...)`.

## Adapters (Entra)

- [x] `screenshots/entra-signin-button.png` — the end-user UI showing the
  "Sign in with Microsoft" button (capability-driven, no local form).
- [ ] `screenshots/entra-config.png` — the DB-backed adapter-config admin page.
  **Blank out the client secret.**
- [ ] `screenshots/entra-audit.png` — the admin audit-log view in Entra mode
  (reads Graph rather than the local table).
