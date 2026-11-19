# M2 Report — Identity, tenancy, audit and licensing

**Date:** 2026-11-19 · **Branch:** `dev` (merged to `main` when green) · **Evidence:** GitHub Actions, PostgreSQL 17 service container

## Gate (spec §9): "prove tenant A cannot read/write tenant B data; unauthorized modules cannot load"

| Gate item | Evidence (integration tests, real PostgreSQL) | Result |
|---|---|---|
| Tenant A cannot read B's rows | `TenantIsolationTests.Tenant_sees_only_its_own_rows`; `AccessApiTests.Tenant_admin_works_in_own_tenant_and_sees_nothing_of_another` | ✅ CI |
| Tenant A cannot insert/update/delete B's rows | `Tenant_cannot_insert_rows_for_another_tenant`, `Tenant_cannot_update_or_delete_another_tenants_rows` (RLS, error 42501 / 0 rows) | ✅ CI |
| Missing context sees nothing (fail closed) | `Missing_context_sees_nothing_and_cannot_write` | ✅ CI |
| Every tenant table has forced RLS | `Every_table_with_tenant_id_has_forced_row_level_security` (catalog query) | ✅ CI |
| Cross-tenant references impossible | `Role_assignment_cannot_reference_another_tenants_role` (composite FK) | ✅ CI |
| Unentitled modules not routable / not in navigation | `LicensingApiTests.Unentitled_module_is_not_routable_and_not_in_navigation` (404) | ✅ CI |
| Entitlement per tenant; expiry read-only, never deleted | `Entitled_module_is_routable_and_in_navigation_for_that_tenant_only`, `Expired_entitlement_is_read_only_never_gone` | ✅ CI |

## Delivered
| Ticket | What |
|---|---|
| M2-01 | `core.tenancy`: tenants (platform-administered, pinned to deployment cell), organizations, sites (IANA time zones), departments |
| M2-02 | Request context (user, tenant, platform scope) from the session only; copied into each PostgreSQL connection; membership and tenant status re-validated per request |
| M2-03 | Forced RLS on all tenant tables + catalog test |
| M2-04 | Cross-tenant matrix at DB and API level (tests above) |
| M2-05 | `core.identity`: global users, tenant memberships, tenant roles with permissions, assignments (site-scope stored), platform operators |
| M2-06 | Permission evaluator: platform permissions only in platform scope + operator + MFA; tenant permissions via roles; unknown → deny; tenant roles cannot contain platform permissions |
| M2-07 | MFA enforcement from OIDC `amr` (platform actions always; tenant actions where declared) |
| M2-08 | `core.audit`: append-only hash chain per tenant + platform chain; same-transaction audit; tamper detection; denials audited |
| M2-09 | `core.licensing`: entitlements with validity and grace; audited changes |
| M2-10 | Entitlement gate (404 / read-only) + permission-filtered navigation manifest |
| M2-11 | Expiry → read-only (never deleted) |
| M2-12 | admin-web: tenant switcher, structure, members & roles, licenses, audit log with integrity check, platform tenant admin |
| M2-13 | Break-glass: time-boxed (≤4 h), dual-approved, read-only, MFA, tenant-visible in the tenant's own audit chain |

Decision record: [ADR-0006 SQL-first migrations and database-enforced security](../adrs/0006-sql-first-migrations-and-database-security.md).

## Tests executed (CI)
- .NET: **95 / 95 passed, 0 skipped** (run 44474557153): Module SDK 30, architecture 4, core-api host 21, integration 40 (database isolation 10, tenant isolation 8, access API 7, audit 6, licensing 5, break-glass 4).
- ai-service, web (7 tests incl. axe), security (gitleaks, SBOM): passed.
- e2e browser login: see "Known gaps" until green.

## Bugs found by the new tests and fixed
1. EF Core did not know the tenant-scoped foreign keys → insert order wrong → tenant-admin appointment returned 500. Relationships now modelled.
2. Dev proxy rewrote the Host header → OIDC callback returned to the API origin. Proxy fixed; `Authentication:Oidc:PublicOrigin` added for proxies/load balancers.

## Security notes
- Platform operators have **no** implicit access to tenant data; only break-glass (read-only, dual approval, audited in the tenant's chain).
- Tenant structure and (future) clinical tables use `tenant_only` policies: invisible even in platform scope.
- The audit trail is tamper-evident, not tamper-proof against a database superuser; production should additionally ship audit events to WORM storage (backlog).

## Known gaps / follow-ups
- e2e login journey in CI: being fixed (see run status).
- Site-scoped role assignments are stored but not yet used in decisions (needs site-bound resources, M3+).
- Plans/bundles and usage meters: M9 (commercial packaging) and M4 (AI metering).
- Logout ends the app session but not always the Keycloak SSO session from the SPA.
- GitHub Actions: bump actions off Node 20.
- Owner's PC still cannot run .NET locally (Smart App Control) — CI is the source of truth.
