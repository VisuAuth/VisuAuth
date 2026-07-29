# Audit log

An opt-in trail of who did what: sign-ins (including failures and lockouts), user
CRUD, role changes, password resets, session revocations. It powers the
`/visuauth/admin/audit-log` screen and the dashboard's activity feed and 7-day
sign-in chart.

## Enabling it

```csharp
builder.Services.AddVisuAuthAuditLog();
```

That's the whole opt-in. Without it, a no-op writer absorbs every audit call at
zero cost — so VisuAuth's own handlers never have to check whether auditing is
on, and enabling it later needs no code changes anywhere else.

Rows land in a `VisuAuthAuditLog` table on **your** `DbContext`. The table is
part of the model whether or not you enable the plugin, so consumer migrations
stay identical across deployments — only the writing changes. Add a migration
if you're adopting a VisuAuth version that introduced it:

```bash
dotnet ef migrations add AddVisuAuthAuditLog
```

## Retention

Entries are pruned by a background service. Default retention is **90 days**:

```csharp
builder.Services.AddVisuAuthAuditLog(options =>
{
    options.RetentionDays = 365;
});
```

Pick a number that matches your compliance obligations rather than the default —
audit data is exactly the kind of thing that's either kept deliberately or
regretted later.

## What gets recorded

Each entry carries the action, the target (type, id, label), the outcome
(success / failure) with a failure reason, the actor (user id, email, IP, user
agent), a timestamp, the tenant when multi-tenancy is on, and a small JSON
payload for action-specific detail such as the sign-in channel (`web` / `api`).

Notably, **failed** sign-ins are recorded too, along with lockouts and two-factor
hops. That's the half that matters for spotting credential stuffing — a log of
only successful logins tells you nothing about an attack in progress.

> **The actor is captured from the request.** IP and user agent come from
> `HttpContext`, so behind a reverse proxy configure forwarded headers or the
> recorded IP will be your proxy's rather than the caller's.

## Reading it

`/visuauth/admin/audit-log` filters by action, actor, target, and date range. See
[the admin UI tour](admin-ui.md#audit-log-visuauthadminaudit-log).

The dashboard reads the same store for its activity feed and its per-day sign-in
counts. Skip the plugin and those degrade to an empty state rather than an error.

## Privacy

The log records emails, IPs, and user agents — personal data under GDPR and
similar regimes. Two things follow: set `RetentionDays` to something you can
justify, and remember that deleting a user does **not** erase their audit
history, which is usually the point of an audit log but is worth stating in your
own privacy notice.

## Entra deployments

Entra-backed deployments have a different source of truth: the tenant's own
sign-in and directory audit logs. Opt into surfacing those instead with
`AddVisuAuthEntraAuditLog()`, which reads Microsoft Graph rather than a local
table — it needs `AuditLog.Read.All` (admin-consented) and an Entra ID P1
licence, and degrades to an empty view without them. See the
[Entra ID adapter](adapters/entra-id.md).

> **Capture note.** This page has no screenshots yet — the audit-log admin
> capture is tracked in
> [`docs/CAPTURE_CHECKLIST.md`](https://github.com/VisuAuth/VisuAuth/blob/main/docs/CAPTURE_CHECKLIST.md).
