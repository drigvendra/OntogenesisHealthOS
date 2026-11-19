# Module Boundaries, Manifests and Cross-Product Contracts

## 1. Two levels of "loading"

| Level | Question | Mechanism |
|---|---|---|
| **Package** (build time) | Is the module's code in this deployment at all? | Bundle build profile selects which module projects are compiled into the image. A CareOps-only image contains no ClinicOS assemblies. |
| **Tenant** (runtime) | Is the module active for this tenant/site? | `active = present_in_package ∧ dependencies_resolved ∧ entitlement_valid ∧ enabled_by_tenant_admin` |

A module that isn't active for a tenant: its API routes return 404, its UI routes are absent from the navigation manifest, its event handlers skip that tenant's events, and its background jobs don't run for that tenant. **Its data is retained.**

## 2. Project layout per module (.NET)

```
modules/careops-quality/
  module.json                         # manifest
  src/CareOps.Quality.Contracts/      # PUBLIC: DTOs, commands, queries, events, service interfaces
  src/CareOps.Quality/                # INTERNAL: domain, application, EF DbContext, endpoints
  src/CareOps.Quality.Migrations/     # schema "careops_quality"
  tests/CareOps.Quality.Tests/
  web/                                # optional UI route package consumed by product web apps
```

### Reference rules (enforced by architecture tests + MSBuild check)
1. `X` may reference `X.Contracts`, platform SDK packages, and `Y.Contracts` **only if** `Y` is in `X`'s manifest `dependencies`.
2. Nothing may reference `X` (internal) except the host composition root.
3. A `product` module may not depend on another `product` module, except a `pack` on its parent product. Cross-product features go into `bridge` modules.
4. `platform` modules may not depend on `domain`, `product`, `pack` or `bridge` modules.
5. Each module's DbContext maps only tables in its own schema.

## 3. Manifest (`module.json`) — schema v1

```json
{
  "$schema": "https://schemas.ontogenesis.health/module-manifest/v1.json",
  "id": "careops.quality",
  "kind": "product",
  "version": "1.0.0",
  "displayName": "CareOps Quality",
  "requiresCore": ">=1.0.0 <2.0.0",
  "dependencies": [
    { "id": "core.documents", "version": "^1.0.0" },
    { "id": "core.workflows", "version": "^1.0.0" }
  ],
  "optionalDependencies": [
    { "id": "ai.sop-copilot", "version": "^1.0.0" }
  ],
  "incompatibleWith": [],
  "license": { "productCode": "CAREOPS", "feature": "quality.core" },
  "capabilities": ["controlled-documents", "capa", "deviations", "training"],
  "permissions": [
    { "code": "careops.document.read",    "description": "Read controlled documents" },
    { "code": "careops.document.approve", "description": "Approve controlled documents", "requiresESignature": true }
  ],
  "contracts": {
    "provides": {
      "api":    [{ "name": "careops-quality-api", "version": "1.0.0", "spec": "contracts/api/careops-quality.v1.yaml" }],
      "events": [{ "type": "careops.document.published", "version": 1, "schema": "contracts/events/careops/document-published.v1.json" }]
    },
    "consumes": {
      "events": [{ "type": "core.user.deactivated", "version": 1 }]
    }
  },
  "database": { "schema": "careops_quality", "migrations": "CareOps.Quality.Migrations" },
  "ui": {
    "routes": [{ "path": "/quality/documents", "permission": "careops.document.read", "app": "quality-web" }]
  },
  "configuration": { "schema": "config/careops-quality.settings.schema.json" },
  "dataRetention": { "defaultPolicy": "regulated-record", "destructiveUninstall": false },
  "healthChecks": ["db", "object-storage"],
  "lifecycle": { "install": "InstallHook", "upgrade": "UpgradeHook", "disable": "DisableHook", "uninstall": "UninstallHook" }
}
```

Validation of the manifest itself (JSON Schema) runs in CI; resolution runs at startup (package level) and on every tenant enablement change (tenant level).

## 4. Dependency resolution (core.modules)
1. Load manifests of all modules present in the package.
2. Check `requiresCore`, semver ranges, cycles (fail startup on package-level errors).
3. Per tenant: intersect with entitlements and admin-enabled set; compute closure; reject with explainable errors: `MISSING_DEPENDENCY`, `VERSION_INCOMPATIBLE`, `INCOMPATIBLE_COMBINATION`, `NOT_ENTITLED`, `REGION_POLICY_BLOCKED`.
4. Enabling a module that needs others: propose the closure as a draft diff for the admin (never auto-enable licensed modules).
5. Disabling: refused if an active module depends on it; otherwise the module goes `disabled` (data retained, read-only export still available to tenant admin per retention policy).

## 5. Cross-module communication

| Need | Mechanism | Rules |
|---|---|---|
| Synchronous query/command within one deployment | Interface in `Y.Contracts`, implemented in `Y`, resolved via DI | Only if `Y` is a declared dependency. Caller must handle `ModuleNotActive`. |
| Async notification | Transactional **outbox** in producer schema → dispatcher → **inbox** in consumer schema (dedupe by event id) | At-least-once; handlers idempotent. Thin payloads: IDs, type, version, minimal changed fields. |
| Cross-deployment / external | Versioned REST (OpenAPI) and events via HealthConnect | Auth, idempotency keys, mapping. |

### Event envelope (CloudEvents-compatible)
```json
{
  "specversion": "1.0",
  "id": "01J...ULID",
  "type": "careops.document.published",
  "dataschema": "careops/document-published/v1",
  "source": "module/careops.quality@1.0.0",
  "time": "2026-11-18T10:00:00Z",
  "tenantid": "t_...",
  "correlationid": "...",
  "data": { "documentId": "...", "versionId": "...", "effectiveFrom": "2026-12-01" }
}
```

### Contract versioning
- Additive changes = minor version; breaking changes = new major (`v2`) published side by side; producers keep emitting old major for ≥ 1 release train.
- Contract tests (consumer-driven) live in `tests/contracts/`; CI fails if a provider breaks a registered consumer expectation.

## 6. How HospitalOS reuses outpatient capability without copying ClinicOS
- Patient, practitioner, encounter and consent master data live in `clinical.registry` (domain module), not in `clinicos.core`.
- ClinicOS and HospitalOS both depend on `clinical.registry` contracts.
- HospitalOS OPD reuses outpatient *workflows* by depending on published `clinicos.core` **contracts** only if ADR approves a declared dependency; default plan is a `hospitalos.opd` module that composes `clinical.registry` + shared workflow definitions from Configuration Studio.
