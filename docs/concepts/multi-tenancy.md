# Multi-tenancy

VisuAuth supports a **shared database, shared schema** multi-tenancy model: a
`TenantId` discriminator on the Identity tables, an EF Core global query filter
that restricts every query to the current tenant, and a `SaveChanges`
interceptor that stamps `TenantId` on insert.

Multi-tenancy is **opt-in**. Without it, the tenant query filters are no-ops
and the catalogue page does not render — single-tenant apps pay nothing.

> **This page is being expanded for the v1.0 documentation site.**

## Planned outline

- Enabling tenancy: `MultiTenantIdentityUser`,
  `MultiTenantIdentityDbContext<TUser>`, and the `EnableVisuAuthTenancy`
  registration.
- Tenant resolution strategies — subdomain, `X-Tenant-Id` header, admin
  sidebar cookie switcher, and JWT `tenant_id` claim.
- The global query filter and the `SaveChanges` interceptor.
- The tenant catalogue admin page (create / rename / delete).
- Per-tenant configuration: password policy, lockout, branding.
- How per-tenant theming ties in (see [Theming](../theming.md)).
