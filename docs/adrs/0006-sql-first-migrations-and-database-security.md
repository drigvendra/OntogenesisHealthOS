# ADR-0006: SQL-first migrations and database-enforced security

- **Status:** Accepted (implemented in M1/M2)
- **Date:** 2026-11-19
- **Supersedes:** the EF Core migrations part of ADR-0001

## Context
- Tenant isolation, module isolation and the audit trail are enforced by PostgreSQL itself (row-level security, per-module roles, SECURITY DEFINER functions). EF Core migrations express these poorly (raw SQL inside C#, snapshot drift).
- `dotnet ef` cannot load our assemblies on the owner's development machine (Windows Smart App Control), which forced hand-written snapshots.
- Regulated customers (GxP) want reviewable, immutable change scripts.

## Decision
1. **Schema changes are plain SQL files** per module: `Migrations/NNNN_description.sql`, embedded in the module assembly, applied in order by `ModuleMigrator` as the migrator role, one transaction per script, recorded in `<schema>.schema_migrations` with a SHA-256 checksum. **Editing an applied script fails the migration**; changes are always new scripts. EF Core is used for queries only; an integration test checks every EF-mapped column exists with matching nullability.
2. **Roles** (prefix configurable per installation):
   | Role | Login | Purpose |
   |---|---|---|
   | `hos_migrator` | yes, NOINHERIT, CREATEROLE | owns schemas, runs migrations |
   | `hos_app` | yes, NOINHERIT, no grants | runtime login; must `SET ROLE` to a module role |
   | `hos_mod_<schema>` | no, INHERIT | one per module: DML on its own schema only |
   | `hos_platform_services` | no | shared routines (e.g. `core_audit.append`); the only role module roles inherit from |
3. **Session context**: every module connection runs `SET ROLE hos_mod_<schema>` and sets `app.tenant_id`, `app.user_id`, `app.platform` from the server-side request context. RLS policies read these; unset values match nothing (fail closed).
4. **RLS policy patterns**:
   - `tenant_only` — patient, clinical and regulated data, and tenant-internal structure. Platform operators cannot see these rows.
   - `tenant_or_platform` — platform-administered tenant data (memberships, roles, entitlements).
   - Extra `FOR SELECT` policies only where a user must see their own rows across tenants (memberships).
   - Composite foreign keys `(tenant_id, id)` prevent cross-tenant references.
5. **Audit trail**: rows are written only via `core_audit.append()` (SECURITY DEFINER), which takes tenant and actor from the session, serialises per chain with an advisory lock, and stores a SHA-256 hash chain. No role has UPDATE/DELETE. `core_audit.verify()` detects tampering, including by superusers. Modules append in the same transaction as the business change (`SaveChangesWithAuditAsync`).

## Consequences
- Migrations must be written by hand in SQL and reviewed; every new tenant-scoped table needs RLS (an integration test fails otherwise).
- Function definitions must `REVOKE ... FROM PUBLIC` explicitly.
- Down-migrations are not provided; rollback is restore-from-backup or a forward fix (consistent with ADR deployment notes).
