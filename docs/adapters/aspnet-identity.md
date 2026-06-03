# ASP.NET Core Identity adapter

This is the default backend and the one most consumers use. It implements every
VisuAuth contract against `UserManager`, `SignInManager`, and `RoleManager`,
querying the standard `AspNet*` tables through EF Core. It declares full
capabilities — local login, registration, password reset, 2FA reset, role
mutation, session revocation, and more.

It is wired automatically by `AddVisuAuth<TUser>()`. See
[Getting started](../getting-started.md) for the complete setup.

> **This page is being expanded for the v1.0 documentation site.**

## Planned outline

- Capability surface for the Identity adapter (every supported flag).
- How temporary passwords are generated and surfaced to the admin.
- Lockout / enable / disable semantics.
- Session revocation via security-stamp rotation.
- Customizing the Identity options VisuAuth respects (password policy,
  lockout window).
