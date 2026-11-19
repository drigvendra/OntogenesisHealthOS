# ADR-0002: Multi-tenancy and data isolation

- **Status:** Proposed
- **Date:** 2026-11-18

## Context
Need strong tenant isolation (spec §5), data residency for India and EU, small-clinic economics, and the option to isolate pharma/hospital customers fully.

## Decision
1. **Regional deployment cells.** Each cell (e.g. `IN-1`, `EU-1`) is a complete deployment. Tenants are pinned to one cell at onboarding. No PHI crosses cells. A small global control plane (tenant directory, licensing master) holds **no PHI**.
2. **Shared database per cell, schema per module, `tenant_id` + PostgreSQL RLS (FORCED)** as the default.
3. **Tenant context** is set per transaction (`SET LOCAL app.tenant_id`) from the authenticated principal by a DB connection interceptor; EF Core global query filters as a second layer. Fail closed when unset.
4. **Per-module DB roles** with grants limited to the module's own schema; migration role separate from runtime role; runtime roles are not table owners and lack `BYPASSRLS`.
5. **Escalation path:** `dedicated database` and `dedicated cell` topologies use the same code with tenant→connection routing.
6. Organization hierarchy: `tenant → organization → site → department`; ABAC attributes (site, department) restrict within a tenant.

## Alternatives
- **Database-per-tenant everywhere:** strongest isolation but costly for thousands of small clinics, heavy migrations. Kept as an option.
- **Schema-per-tenant:** conflicts with schema-per-module boundaries and scales poorly.
- **Application-only filtering:** rejected; spec explicitly forbids relying on it.

## Consequences
- Every tenant-owned table needs an RLS policy + test (template + arch test to detect tables without policies).
- Connection pooling per module role; watch pool counts.
- Cross-tenant reporting for platform operators uses aggregated, PHI-free metrics only.
