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

- [ ] `screenshots/admin-dashboard.png` — `/visuauth/admin`, light theme, a few
  seeded users visible. *(Used on `index.md` and as the hero.)*
- [ ] `screenshots/enduser-login.png` — `/visuauth/login`, default theme.
  *(Used on `getting-started.md`.)*

## Admin UI

- [ ] `screenshots/admin-user-list.png` — user list with search box and
  pagination, 3+ users.
- [ ] `screenshots/admin-user-list-search.gif` — typing in search → live
  (htmx) filtering of the list.
- [ ] `screenshots/admin-user-detail.png` — a single user's detail page
  (claims, roles, lockout / 2FA state).
- [ ] `screenshots/admin-user-create.png` — the create-user form.
- [ ] `screenshots/admin-user-actions.png` — the lock / reset-password /
  force-logout / reset-2FA actions on a user.
- [ ] `screenshots/admin-roles.png` — the roles catalogue.
- [ ] `screenshots/admin-role-assign.gif` — assigning a role to a user.
- [ ] `screenshots/admin-dashboard-dark.png` — `/visuauth/admin` in dark theme
  (pairs with `admin-dashboard.png`).

## End-user UI

- [ ] `screenshots/enduser-login-dark.png` — `/visuauth/login`, dark theme.
- [ ] `screenshots/enduser-register.png` — `/visuauth/register`.
- [ ] `screenshots/enduser-forgot-password.png` — `/visuauth/forgot-password`.
- [ ] `screenshots/enduser-reset-password.png` — `/visuauth/reset-password`.
- [ ] `screenshots/enduser-password-toggle.gif` — the show/hide password eye
  toggling.
- [ ] `screenshots/enduser-lang-switcher.gif` — toggling pt-BR ⇄ en via the
  flag switcher.

## Multi-tenancy

- [ ] `screenshots/tenants-catalogue.png` — the tenant catalogue admin page.
- [ ] `screenshots/tenant-switcher.gif` — the admin sidebar tenant switcher
  changing scope.
- [ ] `screenshots/tenant-create.png` — creating a tenant.

## Theming

- [ ] `screenshots/theming-default.png` — default theme baseline (for
  before/after pairs).
- [ ] `screenshots/theming-css-tokens.png` — layer 1: re-branded via CSS
  variables (custom primary color + logo).
- [ ] `screenshots/theming-light-dark.gif` — the built-in light/dark toggle.
- [ ] `screenshots/theming-per-tenant.png` — layer 4: two tenants rendering
  with different brand colors.

## Mobile & JWT

- [ ] `screenshots/mobile-webview-callback.png` — the WebView callback preview
  page.

## Adapters (Entra)

- [ ] `screenshots/entra-signin-button.png` — the end-user UI showing the
  "Sign in with Microsoft" button (capability-driven, no local form).
- [ ] `screenshots/entra-config.png` — the DB-backed adapter-config admin page.
- [ ] `screenshots/entra-audit.png` — the admin audit-log view.
