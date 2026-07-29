# Two-factor authentication

VisuAuth ships TOTP (authenticator-app) two-factor as part of the end-user
surface: enrolment, the sign-in challenge, and recovery codes. It's driven by
ASP.NET Core Identity's own token providers — VisuAuth adds the pages, the QR
code, and the admin-side reset.

Available whenever the backend declares `SupportsTwoFactor`, which the
[Identity adapter](adapters/aspnet-identity.md) does. Entra-backed deployments
declare it `false` — Microsoft owns authenticator enrolment there — so the pages
and controls hide themselves rather than half-working.

## Pages

| Route | Purpose | Access |
|---|---|---|
| `/visuauth/two-factor/setup` | Pair an authenticator app | signed-in user |
| `/visuauth/two-factor/verify` | The challenge during sign-in | anonymous |
| `/visuauth/two-factor/recovery-codes` | View / regenerate recovery codes | signed-in user |

The **challenge** page is deliberately anonymous: it runs mid-sign-in, after the
password succeeded but before the user holds a full identity. Requiring
authentication there would break the flow it exists to complete. Setup and
recovery codes require a signed-in user, as you'd expect — see
[Securing the admin](securing-the-admin.md#the-end-user-pages-stay-anonymous)
for how those levels survive a global fallback policy.

## Enrolment

`/visuauth/two-factor/setup` renders the shared secret as an inline SVG QR code
(via QRCoder — no external image service, no outbound call), plus the manual
key for authenticator apps that prefer typing it. Scanning it and entering one
generated code completes enrolment and reveals the recovery codes.

The label your users see in their authenticator app comes from
`TwoFactorIssuerOptions`:

```csharp
builder.Services.Configure<TwoFactorIssuerOptions>(options =>
{
    options.Issuer = "Acme Corp";   // defaults to "VisuAuth"
});
```

Set this to your product name before shipping. It's baked into the `otpauth://`
URI at enrolment time, so changing it later doesn't rename existing enrolments —
users who already paired keep seeing the old label.

## Recovery codes

Generated at enrolment and shown once. `/visuauth/two-factor/recovery-codes`
lets a signed-in user regenerate them, which invalidates the previous set.
Treat them as password-equivalent: each one is single-use and bypasses the
authenticator entirely.

## Resetting a user's 2FA as an operator

From the user's detail page in the [admin UI](admin-ui.md#user-detail-visuauthadminusersid),
*Reset two-factor* disables 2FA and resets the authenticator key, so the user
enrols again from scratch. Use it when someone loses their device and their
recovery codes.

This is capability-gated on `SupportsTwoFactorReset`. Entra declares it `true`
even though it declares `SupportsTwoFactor` false — the admin *can* wipe a
directory user's registered authentication methods through Graph, forcing
re-enrolment through Microsoft's own surfaces. It needs the
`UserAuthenticationMethod.ReadWrite.All` application permission.

## Interaction with the mobile API

`POST /visuauth/api/auth/login` answers `401` with
`"Two-factor authentication is required."` instead of issuing a token, so a
native client can tell a 2FA requirement apart from a wrong password.

> **The REST channel can't complete the challenge.** There is no
> `api/auth/two-factor` endpoint — `/login`, `/register`, and `/refresh` are the
> whole surface. A native app whose users have 2FA enabled should use the
> [WebView deep-link flow](mobile.md#flow-2-the-webview-deep-link-flow), which
> runs the real `/visuauth/two-factor/verify` page and hands back a token at the
> end. Purely-REST clients only work for accounts without 2FA.

> **Capture note.** This page has no screenshots yet — the setup / challenge
> captures are tracked in
> [`docs/CAPTURE_CHECKLIST.md`](https://github.com/VisuAuth/VisuAuth/blob/main/docs/CAPTURE_CHECKLIST.md).
