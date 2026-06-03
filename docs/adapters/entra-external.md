# Microsoft Entra External ID adapter

The `VisuAuth.EntraExternal` adapter targets **Microsoft Entra External ID**
(customer / CIAM tenants). It mirrors the Entra ID adapter's contract surface —
the app-role Graph shape is identical across tenant families — while adapting
to the External ID specifics (tenant domain, customer user flows).

> **This page is being expanded for the v1.0 documentation site.**
> The package-level references already live in
> [`src/VisuAuth.EntraExternal/README.md`](https://github.com/VisuAuth/VisuAuth/blob/main/src/VisuAuth.EntraExternal/README.md)
> and
> [`src/VisuAuth.EntraExternal.Web/README.md`](https://github.com/VisuAuth/VisuAuth/blob/main/src/VisuAuth.EntraExternal.Web/README.md).

## Planned outline

- How External ID differs from workforce Entra ID, and what that means for
  the adapter.
- Configuring `EntraExternalOptions` (tenant domain, app-role resource).
- Cursor-based pagination over Graph.
- The DB-backed adapter-config admin UI.
- Claims → Graph user attribute mapping.
- User-flow management status (blocked on Graph v1.0 — see the project
  roadmap).
