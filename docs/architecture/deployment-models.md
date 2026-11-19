# Deployment Models

## 1. Bundle build profiles
A bundle profile is a file in `deploy/bundles/<name>.json` that lists which modules are compiled into the core-api image and which web apps ship. CI builds and tests every profile in a matrix.

| # | Bundle | Modules in package | Web apps | Patient data? |
|---|---|---|---|---|
| 1 | CareOps only | core.*, careops.quality, ai.sop/quality/knowledge-copilot | admin-web, quality-web | No |
| 2 | ClinicOS only | core.*, clinical.registry, clinicos.core, clinicos.billing, ai.clinical-doc/workflow-copilot | admin-web, clinic-web | Yes |
| 3 | Dental clinic | #2 + specialty.dental | admin-web, clinic-web | Yes |
| 4 | Eye clinic | #2 + specialty.vision | admin-web, clinic-web | Yes |
| 5 | Hospital (future) | core.*, clinical.registry, hospitalos.* (selected), optional careops.quality + bridges | admin-web, hospital-web, (quality-web) | Yes |
| 6 | AI add-on only | core.*, core.ai-gateway, healthconnect.*, ai.knowledge/workflow/mapping-copilot | admin-web | Depends on connected system |
| 7 | Pharma | #1 with GxP regulatory profile and validated-deployment package (IQ/OQ evidence) | admin-web, quality-web | No |
| 8 | MedTech | core.*, medtechos.*, optional careops.quality + bridge | admin-web, medtech-web | No (normally) |

A SaaS cell can ship the **full** package (all modules) and rely on tenant-level activation; dedicated/on-prem customers get the minimal package for their bundle. Both must work — the minimal-package build is the proof of independence.

## 2. Hosting topologies

| Topology | Who | Isolation | Notes |
|---|---|---|---|
| **Shared SaaS cell** | Clinics, SMB QA teams | Shared DB with RLS + schema-per-module | Default; one cell per residency region (IN-1, EU-1). |
| **Dedicated database** | Larger customers | Own PostgreSQL DB/cluster, shared app tier | Tenant→connection routing in core.tenancy. |
| **Dedicated cell** | Pharma (GxP), hospitals | Own app + DB + storage + keys | Validated release cadence, change control. |
| **Customer-hosted (on-prem / customer cloud)** | Hospitals with residency mandates | Fully separate | Docker Compose (small) or Helm (later); offline licence file; no mandatory Kubernetes. |

## 3. Environments
`local` (Docker Compose, synthetic data) → `ci` (ephemeral Testcontainers) → `staging` (synthetic data, production-like) → `production` (only after M12 gates and written approval).

## 4. Release & versioning
- Platform core and each module carry semver; a release train publishes a tested **compatibility matrix** (core x.y + module versions per bundle).
- DB migrations are forward-only with expand/contract pattern; down-migrations only where safe, otherwise restore-from-backup is the rollback path (tested).
- Independent product versioning: products release on their own trains against a pinned core range.
