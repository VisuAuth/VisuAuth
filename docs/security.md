# Security posture

VisuAuth is an authentication library, so its security properties are part of
its contract, not an afterthought. This page is the concise threat model: for
each flow it names the main threats, the mitigation, and where that mitigation
is enforced and tested. It is meant to be read before changing any auth code —
and kept honest, so if a mitigation here is not actually enforced, that is a
bug.

> Reporting a vulnerability: see
> [SECURITY.md](https://github.com/VisuAuth/VisuAuth/blob/main/SECURITY.md) for
> the private disclosure process. Do not open a public issue for a suspected
> vulnerability.

## Trust boundary

The only trusted input is what an authenticated principal legitimately controls.
Everything a client sends — request bodies, query strings, `returnUrl`, the
`X-Tenant-Id` header, bearer tokens — is untrusted until validated. Signed
tokens are trusted only *after* signature validation.

## Flows

### Password sign-in (web + API)

- **Threats:** credential stuffing, user enumeration, brute force.
- **Mitigations:** ASP.NET Core Identity lockout; a single generic error for
  wrong-password and unknown-email so responses do not reveal which accounts
  exist; anti-forgery on the web form.
- **Enforced in:** `Login.cshtml.cs`, `AuthApi.LoginAsync`, the Identity
  adapter's `SignInManager` usage.
- **Tested in:** `LoginPageTests`, `AuthApiTests`
  (`PostLogin_WithWrongPassword_*`, `PostLogin_WithUnknownEmail_ReturnsSameGeneric401`).

### JWT issuance, validation, and refresh

- **Threats:** token forgery, algorithm stripping (`alg:none`), issuer/audience
  confusion, refreshing a token for an arbitrary `sub`.
- **Mitigations:** HS256 with signed tokens required; issuer + audience
  validated; the same validation parameters back the bearer scheme and the
  refresh validator so they cannot drift; refresh re-authenticates the presented
  token (signature/issuer/audience) before minting a new one, skipping only the
  lifetime check. Key rotation is supported via `AdditionalValidationKeys`
  without invalidating tokens in flight.
- **Enforced in:** `JwtServiceCollectionExtensions`, `AspNetIdentityJwtValidator`,
  `AuthApi.RefreshAsync`.
- **Tested in:** `AuthApiTests` (forged signature, wrong issuer, expired-but-authentic),
  `JwtHardeningTests` (`alg:none`, missing `sub`), `AspNetIdentityJwtValidatorTests`,
  `JwtKeyRotationTests`.

### Session / token revocation

- **Threats:** a leaked or stolen token staying valid after the user is locked
  out, password-reset, or "revoke sessions" is clicked — including a revoked
  token being *exchanged at `/refresh`* for a fresh one ("laundering").
- **Mitigations:** every JWT carries the user's security stamp (`visuauth_stamp`).
  Rotating the stamp revokes outstanding tokens on **both** paths: the bearer
  scheme compares it on every authenticated request (default on), and
  `/refresh` compares the presented token's stamp before reissuing. The refresh
  comparison **fails closed** — a token with no stamp claim is rejected rather
  than treated as a match. Lockout is re-checked at issue and refresh time.
- **Enforced in:** `JwtServiceCollectionExtensions` (`OnTokenValidated`),
  `AspNetIdentityJwtIssuer.ReissueAsync`.
- **Tested in:** `JwtSecurityStampTests` — both the protected-endpoint path and
  the refresh path, plus the stampless-token case.
- **Residual:** revocation is next-request, not instant; keep `LifetimeMinutes`
  short. Opt out with `JwtOptions.ValidateSecurityStamp = false` only if you
  accept expiry-bounded revocation.

### Admin dashboard

- **Threats:** anonymous access to user administration (list, reset, delete,
  role assignment).
- **Mitigations:** every page under `/visuauth/admin` requires the
  `VisuAuth.Admin` policy — "authenticated user" by default, tightenable to a
  role, with an explicit `AllowAnonymousVisuAuthAdmin()` opt-out. Mutations are
  POST + anti-forgery.
- **Enforced in:** `VisuAuthAdminUiServiceCollectionExtensions` (`AuthorizeFolder`).
- **Tested in:** `VisuAuthAdminAuthorizationTests`.

### WebView deep-link callback

- **Threats:** turning the login page into an open-redirect gadget; leaking the
  token to an attacker-controlled URL.
- **Mitigations:** only non-HTTP custom schemes on an allow-list are honored as
  deep links (`http`/`https` are rejected even if listed); web `returnUrl` goes
  through the local-URL guard; the token defaults to the URL fragment.
- **Enforced in:** `Login.cshtml.cs` (`TryParseAllowedDeepLink`, `SanitiseLocalReturnUrl`).
- **Tested in:** `WebViewCallbackTests`, `LoginPageTests` (external `returnUrl`
  is not honored).

### Password reset & email confirmation

- **Threats:** user enumeration via the forgot-password response; token reuse.
- **Mitigations:** forgot-password always returns the same generic response and
  only surfaces a token in development mode; reset/confirm rely on Identity's
  single-use, time-limited tokens.
- **Enforced in:** `ForgotPassword.cshtml.cs`, the Identity adapter.
- **Tested in:** `RegisterAndResetTests`.

### Localization / culture switch

- **Threats:** open redirect through the culture-switch `returnUrl`.
- **Mitigations:** the endpoint accepts only local URLs.
- **Enforced in:** `CultureSwitchEndpoint`.
- **Tested in:** `LocalizationTests` (external `returnUrl` rejected).

### Multi-tenancy

- **Mitigations today:** a global EF query filter scopes queries to the resolved
  tenant; a `SaveChanges` interceptor stamps `TenantId` on insert.
- **Known limitation:** the tenant is resolved from a client-controlled header /
  cookie and is **not** bound to the authenticated principal. This is a
  deliberate open design decision — see
  [#79](https://github.com/VisuAuth/VisuAuth/issues/79). Until it is resolved, do
  not rely on the header/cookie resolver alone as a security boundary between
  tenants for authenticated end-user API calls.

## Open design decisions

Tracked in [#79](https://github.com/VisuAuth/VisuAuth/issues/79):

- Binding the resolved tenant to the authenticated principal (the admin tenant
  switcher intentionally switches, and middleware ordering affects multi-tenant
  login, so the trust model needs a deliberate decision).
- Opaque, revocable, single-use refresh tokens (today refresh reissues from the
  access token).

## A rule for contributors

A code comment that asserts a security property ("validates the signature",
"prevents open-redirect", "invalidates outstanding tokens") must be backed by a
test that fails if the property is removed. A comment that claims a guarantee
the code does not enforce is worse than no comment — it makes reviewers trust
something that is not there.
