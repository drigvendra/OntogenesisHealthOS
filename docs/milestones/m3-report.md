# M3 Report — Module SDK and Configuration Studio

**Date:** 2026-11-19 · **Branch:** `dev` → `main` when green · **Evidence:** GitHub Actions (PostgreSQL 17, Keycloak, Chromium)

## Gate (spec §9): "instantiate five distinct customer profiles without code edits; each loads correct modules, forms and permissions; disabling a module preserves records"

| Gate item | Evidence | Result |
|---|---|---|
| Five profiles without code edits | `OnboardingTests.Template_instantiates_the_expected_tenant_without_code_changes` × 5 (general clinic, dental clinic, eye clinic, pharma QA, equipment service): exact module set, forms, workflows, roles, navigation, profile-driven settings; re-apply is idempotent | ✅ CI |
| Correct permissions per profile | Template roles created with tenant-scoped permissions only; navigation filtered by permission | ✅ CI |
| Disabling preserves records | Disabling only flips `core_modules.tenant_module.enabled`; module gate returns 404 while disabled and access returns on re-enable; audit records `dataRetained: true` (`ModulesApiTests`) | ✅ CI |
| Each bundle ships only its product modules | CI `bundles` matrix (careops, clinicos, dental-clinic, eye-clinic, medtech): `modules list` and assembly inspection | ✅ CI |

## Delivered
| Ticket | What |
|---|---|
| M3-01 | Manifest model extended (settings); validator rules for settings and platform permissions |
| M3-02 | Tenant activation resolver: enabled ∧ licensed ∧ dependencies active ∧ compatible; explainable reasons; enable plan with dependency closure |
| M3-03 | `core.modules`: per-tenant enable/disable (tenant admin and operator onboarding), dependents block disabling, audited |
| M3-04 | Module discovery from shipped assemblies; bundle profiles `deploy/bundles/*.props`; `modules list`; CI bundle matrix |
| M3-05/06 | Settings declared in manifests; 8-level precedence resolver (locks, narrowing constraints, role merge strategies, effective dating, provenance trace) |
| M3-07 | `core.configuration`: drafts → preview → approve (four-eyes setting, locked on by the pharma profile) → publish immutable snapshots → rollback as new snapshot; stale-draft detection |
| M3-08 | Declarative forms (typed fields, units, ranges, options, PHI/PII classification, visibleWhen/requiredWhen as data — no code) with value validation |
| M3-09 | Workflow definitions with structural validation (reachability, terminal states, known permissions, e-signature and four-eyes flags); engine in M4 |
| M3-10 | Five onboarding templates; preview (no changes) and idempotent apply via module-owned administration contracts |
| M3-11 | admin-web onboarding wizard (template, country, preview, apply) |
| M3-12 | admin-web: modules with reasons and dependency prompt; configuration with effective values and "why" trace; drafts, publish, rollback, history; forms/workflows list |
| M3-13 | Country profiles IN / EU-DE / EU-FR with UI language setting (en/hi/de/fr); translations of the UI itself are not done yet |
| — | Product skeleton modules (CareOps, ClinicOS, Dental, Vision, MedTechOS): real manifests, licences, permissions, navigation, status endpoint only |
| — | Codespaces dev container + `scripts/dev-*.sh`, exercised by the CI e2e job |

## Tests (CI run 44501590653, commit b593b2a — all jobs green)
- .NET: **149 / 149 passed, 0 skipped** (unit, architecture, host, integration incl. onboarding gate).
- Bundles: 5 / 5. Web: 9 tests incl. accessibility. ai-service, security: passed.
- e2e via the Codespaces scripts (`scripts/dev-start.sh`): **3 / 3** — API anonymous/CSRF checks, password login, **password + one-time code login recognised as MFA**; database setup re-run is idempotent; `grant-operator.sh` works; 2 users provisioned.

## Bugs found by tests and fixed
1. Profile file `country.IN.json` compiled into a satellite assembly (MSBuild read "IN" as a culture).
2. Entitlement cache keyed by feature only; onboarding granted licences after enabling → licences first, cache keyed by tenant.
3. `core.configuration` declared `core.identity ^0.1.0` while identity is 0.2.0 — caught by the package resolver at startup.
4. Audit details compared as text although PostgreSQL normalises `jsonb`.
5. Keycloak emitted no `amr`; dev realm now labels each login step.

## Known gaps
- Regulatory profile values are examples, flagged for counsel review.
- UI string translations (Hindi/German/French) not implemented; only the language setting exists.
- Site-scoped roles still not used in decisions.
- Workflow execution, document storage, AI gateway: M4.
- Product modules are skeletons until their milestones (CareOps M5, ClinicOS M6, packs M7, MedTechOS M11).
