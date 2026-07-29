# Release notes

VisuAuth follows [SemVer](https://semver.org). Until `1.0.0`, minor (`0.x`) bumps
may carry breaking changes — they are always called out explicitly.

The authoritative, complete changelog lives in
[`CHANGELOG.md`](https://github.com/VisuAuth/VisuAuth/blob/main/CHANGELOG.md) at
the repository root.

## Versions

| Version | Scope |
|---|---|
| [`0.2.0`](https://github.com/VisuAuth/VisuAuth/releases/tag/v0.2.0) | Entra ID + Entra External ID adapters, audit log, TOTP, external logins, cursor pagination, adapter-config UI |
| [`0.1.0`](https://github.com/VisuAuth/VisuAuth/releases/tag/v0.1.0) | Admin UI, end-user pages, multi-tenancy, the four theming layers, mobile JWT + WebView |
| `0.0.1-alpha` | Placeholder release reserving the package name |

Every merge to `main` also publishes a `0.x.0-alpha.<run>` pre-release, so you can
track unreleased work without waiting for a tag.

## All packages ship together

The `VisuAuth.*` family shares a single version, even when a given package didn't
change, so dependency graphs stay trivial to reason about. Mixing versions across
the family is not supported.

## Upgrading

Read the `Unreleased` and target-version sections of the
[changelog](https://github.com/VisuAuth/VisuAuth/blob/main/CHANGELOG.md) before
bumping — breaking changes carry a migration note.

Two current items deserve attention:

- **The admin dashboard is locked by default.** It used to render for any
  request. Most apps need no change beyond signing in, but you should restrict it
  to a role — see [Securing the admin](securing-the-admin.md). **Entra
  deployments need the new `VisuAuth.Entra.Web` package** to have anything to
  sign in with.
- **Several authentication fixes landed together**, including a pre-auth account
  takeover on the token-refresh endpoint and a revocation bypass. If you run a
  VisuAuth release older than these, upgrading is not optional — the details are
  in the changelog's Security section, and the model they now defend is in
  [Security posture](security.md).
