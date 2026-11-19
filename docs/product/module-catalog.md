# Module Catalog

Every module ships a `module.json` manifest (schema in `docs/architecture/module-boundaries.md`). This catalog is the human-readable view. **Kind**: `platform` (Core, always present), `domain` (shared domain module, no UI product of its own), `product`, `pack` (extends a product), `bridge` (optional cross-product integration), `ai` (AI capability).

## Platform modules (HealthOS Core)
| Module id | Kind | Depends on | Milestone | Notes |
|---|---|---|---|---|
| `core.identity` | platform | — | M1–M2 | Users, roles, permissions, OIDC integration, MFA policy, service principals |
| `core.tenancy` | platform | core.identity | M2 | Tenant → organization → site → department hierarchy; region/cell assignment |
| `core.audit` | platform | core.tenancy | M2 | Append-only audit events, hash-chained per tenant |
| `core.licensing` | platform | core.tenancy | M2 | Product catalog, plans, subscriptions, entitlements, usage meters |
| `core.modules` | platform | core.licensing | M3 | Manifest registry, dependency resolution, enable/disable lifecycle |
| `core.configuration` | platform | core.modules | M3 | Configuration Studio: settings, forms, workflow definitions, templates, snapshots |
| `core.documents` | platform | core.tenancy | M4 | Object storage, versioning, malware scan hook, retention holds |
| `core.workflows` | platform | core.configuration | M4 | State engine, approvals, SLAs, e-signature primitive |
| `core.notifications` | platform | core.tenancy | M4 | In-app + email; pluggable channels |
| `core.ai-gateway` | platform | core.licensing, core.audit | M4 | Provider abstraction, budgets, metering, review queues, AI audit |

## Domain modules
| Module id | Kind | Depends on | Milestone | Notes |
|---|---|---|---|---|
| `clinical.registry` | domain | core.* | M6 | Patient, practitioner, encounter master records, external IDs, consent. Shared by ClinicOS and HospitalOS so that neither depends on the other. **(Proposed in ADR-0003; needs approval.)** |

## Product modules
| Module id | Kind | Depends on | Licensed as | Milestone |
|---|---|---|---|---|
| `careops.quality` | product | core.documents, core.workflows | CareOps Quality AI | M5 |
| `clinicos.core` | product | clinical.registry, core.workflows | ClinicOS AI | M6 |
| `clinicos.billing` | product | clinicos.core | ClinicOS AI (plan feature) | M6 |
| `specialty.dental` | pack | clinicos.core | Dental pack | M7 |
| `specialty.vision` | pack | clinicos.core | Vision pack | M7 |
| `hospitalos.*` (adt, beds, nursing, orders, pharmacy, lab, radiology, ot, billing, inventory, reporting) | product | clinical.registry (+ per-department deps) | HospitalOS bundle or per-module | M10 |
| `medtechos.*` (assets, maintenance, calibration, field-service, contracts, parts) | product | core.documents, core.workflows | MedTechOS AI | M11 |
| `healthconnect.*` (sdk runtime, mapping, fhir-facade, hl7v2) | product | core.* | HealthConnect | M8 |

## AI capability modules (opt-in, separately entitled)
| Module id | Depends on | Human gate |
|---|---|---|
| `ai.config-copilot` | core.configuration | Admin approves draft diff before publish |
| `ai.sop-copilot` | careops.quality | Author + approver review |
| `ai.quality-copilot` | careops.quality | QA reviewer |
| `ai.knowledge-copilot` | core.documents | Read-only answers with citations |
| `ai.clinical-doc-copilot` | clinicos.core | Clinician attestation before note is final |
| `ai.workflow-copilot` | core.workflows | Suggestions only |
| `ai.mapping-copilot` | healthconnect.mapping | Integration engineer approves mapping + test results |
| `ai.service-copilot` | medtechos.* | Service engineer approves report |

## Bridge modules (examples; built only on demand)
| Module id | Depends on | Purpose |
|---|---|---|
| `bridge.clinicos-careops` | clinicos.core contracts, careops.quality contracts | Raise a deviation/incident from a clinic event; link SOPs in clinic |
| `bridge.hospital-medtech` | hospitalos.inventory contracts, medtechos.assets contracts | Link hospital assets to equipment service records |

## Incompatible / invalid combinations (enforced by resolver)
- Any `pack` without its parent product.
- `ai.clinical-doc-copilot` in a tenant whose region profile has no approved provider with data-residency + DPA flags set.
- `clinicos.core` and `hospitalos.opd` both active **at the same site** (one OPD system of record per site); allowed across different sites.
- Bridge module without both products entitled.

## Suggested bundles (commercial packaging; see deployment-models.md)
CareOps Standalone · ClinicOS General · Dental Clinic · Eye Clinic · Clinic + Quality · Pharma QA (GxP profile) · AI Add-on + HealthConnect · MedTech Service · Hospital (future).
