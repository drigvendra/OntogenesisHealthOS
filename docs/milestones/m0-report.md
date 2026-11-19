# M0 Completion Report — Discovery, ADRs and executable plan

**Date:** 2026-11-18 · **Status:** Complete, **awaiting owner approval** before M1.

## 1. What was inspected
- Repository: contained only the master specification. Not a git repository. No existing source to preserve.
- Environment: Windows 11, 31 GB RAM. Present: Git 2.53, Node 24.15, Python 3.14.4, uv, Docker 29.5 (daemon not running), VS Code. **Missing: .NET SDK, pnpm, gh CLI, psql.**

## 2. Architecture decision summary
1. **Modular monolith** (ASP.NET Core) for business APIs + separate Python **ai-service** + separate .NET **integration-worker**; Postgres outbox/inbox instead of a broker.
2. **Two-level module activation:** bundle build profile (what's in the package) and tenant activation (deps ∧ entitlement ∧ admin enable). Minimal-package builds prove product independence.
3. **Boundaries enforced three ways:** project references (Contracts-only), architecture tests in CI, and **per-module PostgreSQL schemas with per-module DB roles**.
4. **Products never depend on products**; packs depend on their parent; cross-product features are **bridge modules**.
5. **New `clinical.registry` domain module** owns patient/practitioner/encounter/consent so ClinicOS and HospitalOS share contracts without coupling.
6. **Tenancy:** regional deployment cells (IN, EU) → shared DB with schema-per-module and `tenant_id` + FORCED RLS, fail-closed; dedicated DB/cell options for pharma and hospitals.
7. **Configuration:** 8-level precedence with locks, constraints, multi-role merge, effective dating and provenance; immutable published snapshots; rollback by re-publishing.
8. **Entitlements are data**; expiry → grace → read-only, never deletion.
9. **AI:** gateway in core-api → ai-service; fake provider in dev/CI; outputs are drafts that only humans can approve; no side-effecting model tools; ACL-filtered RAG inside SQL.
10. **First product: CareOps Quality AI** (no PHI, lowest regulatory risk), then ClinicOS.

## 3. Uncertainties and dangerous assumptions
| # | Assumption / uncertainty | Why it's dangerous | Proposed handling |
|---|---|---|---|
| 1 | "Independently deployable" can be met by a modular monolith | One binary could quietly couple products | Bundle build matrix + package-inspection tests from M3 |
| 2 | Spec places patient/encounter in `clinicos-core` | HospitalOS would depend on ClinicOS or copy it | `clinical.registry` domain module (**needs your approval**) |
| 3 | AI clinical documentation isn't a medical device | MDR Rule 11 / CDSCO could classify it as SaMD depending on intended use | Strict drafting-only intended use; counsel review before launch |
| 4 | LLM providers can receive PHI | Legal basis, DPAs, cross-border transfer (GDPR, DPDP) | Provider allow-list with PHI/region flags; fake provider by default; ClinicOS AI off until approved |
| 5 | DPDP Rules commencement and retention periods | Phased commencement; sector rules vary by state | Configurable region profiles; counsel to supply values |
| 6 | "EU" as one jurisdiction | Health-data rules differ per member state | Country-level profiles (EU-DE, EU-FR, …) |
| 7 | Keycloak operations | Upgrades, HA, theming are real work | Accept for now; customers may federate their own IdP |
| 8 | Scope vs team size | Spec covers years of work (M10/M11 are multi-year) | Ship CareOps v1 first; re-plan after M5 |
| 9 | Part 11 / Annex 11 needs | E-signature and audit design affects core.workflows | Design e-signature primitive in M4 with GxP requirements in mind |
| 10 | CI platform = GitHub Actions | Not confirmed; no gh CLI, no remote | Confirm |
| 11 | MinIO for local S3 | Community distribution changed in 2025 | SeaweedFS for dev |
| 12 | Python 3.14 on machine | Some AI/DB wheels may lag | Pin 3.13 via uv |

## 4. Deliverables produced
| Spec requirement | File |
|---|---|
| vision | [docs/product/vision.md](../product/vision.md) |
| module catalog | [docs/product/module-catalog.md](../product/module-catalog.md) |
| acceptance criteria | [docs/product/acceptance-criteria.md](../product/acceptance-criteria.md) |
| system context (C4 Mermaid) | [docs/architecture/system-context.md](../architecture/system-context.md) |
| module boundaries, manifests, contracts | [docs/architecture/module-boundaries.md](../architecture/module-boundaries.md) |
| data ownership & tenant isolation | [docs/architecture/data-ownership.md](../architecture/data-ownership.md) |
| configuration precedence & entitlements | [docs/architecture/configuration-and-entitlements.md](../architecture/configuration-and-entitlements.md) |
| deployment models | [docs/architecture/deployment-models.md](../architecture/deployment-models.md) |
| threat model | [docs/security/threat-model.md](../security/threat-model.md) |
| regulatory scope | [docs/compliance/regulatory-scope.md](../compliance/regulatory-scope.md) |
| ADRs (stack, tenancy, plugins, integration, AI) | [docs/adrs/](../adrs/) |
| M1–M3 backlog with effort/risk | [backlog-m1-m3.md](backlog-m1-m3.md) |
| local dev environment (Windows/VS Code/CI) | [docs/operations/local-dev-environment.md](../operations/local-dev-environment.md) |
| repository rules | [CLAUDE.md](../../CLAUDE.md), [README.md](../../README.md) |

## 5. Tests run
None. M0 is documentation only; no code exists. Mermaid diagrams haven't been render-checked.

## 6. Security issues
None in code (no code). Design-level risks are listed in the threat model.

## 7. Known gaps
- No per-product threat models or data-flow diagrams yet (scheduled M5/M6).
- Evidence matrix and risk register not yet created (M1-14).
- Pricing/packaging hypotheses, personas and PRDs (spec §11) not written; they are product inputs I need from you.

## 8. Approval checklist — please confirm or amend
- [ ] **Stack (ADR-0001):** .NET 10 + EF Core / Python 3.13 FastAPI / React 19 + Vite + Tailwind 4 + pnpm / PostgreSQL 17 + pgvector / Keycloak / SeaweedFS (dev).
- [ ] **Tenancy (ADR-0002):** regional cells + shared DB with schema-per-module + FORCED RLS; dedicated options later.
- [ ] **`clinical.registry` module (ADR-0003)** instead of patient data inside clinicos-core.
- [ ] **Product order:** CareOps (M5) before ClinicOS (M6).
- [ ] **CI host:** GitHub Actions? Remote repository URL?
- [ ] **Repo root:** this folder (`OntogenesisHealthOS`) is the repo; spec file stays at root.
- [ ] **Initial LLM provider(s)** for non-PHI capabilities (fake provider until decided).
- [ ] Install .NET 10 SDK and pnpm, and start Docker Desktop (commands in local-dev-environment.md).

## 9. Exact next steps after approval (M1)
1. `git init`, base config files (M1-01).
2. .NET solution + Module SDK v0 + architecture tests (M1-02 → M1-04).
3. Docker Compose stack + migrations + health checks (M1-05 → M1-07).
4. Keycloak + BFF login, logging/redaction, ai-service and admin-web skeletons (M1-08 → M1-12).
5. CI pipeline, verified setup doc, M1 report (M1-13 → M1-15).
