# ONTOGENESIS HEALTHOS AI — MASTER CLAUDE CODE ENGINEERING SPECIFICATION

**Version:** 1.0  
**Owner:** Ontogenesis Technologies Pvt. Ltd.  
**Purpose:** Build a modular, AI-native healthcare and life-sciences software ecosystem in which each commercial product can run independently, products can be combined, and a complete HospitalOS suite can be assembled later.  
**Status:** Engineering blueprint; all production clinical and regulated uses require separate verification and approvals.

---

## 0. YOUR ROLE AND OPERATING RULES

You are Claude Code acting as a principal software architect, product engineer, security engineer, and test architect. Implement this specification as a maintainable repository, incrementally. **Do not generate an entire platform in one pass.** First inspect the repository and environment, then produce architectural decision records (ADRs), a milestone plan, and executable scaffolding. Ask for approval at major milestones. Do not fabricate working integrations, regulatory compliance, or test results. Never deploy to production or process real patient data without explicit authorization.

**Non-negotiable:** Every customer-facing product must be independently deployable, independently licensed, independently usable, and optionally composable with others. The only permitted mandatory dependencies are versioned platform services and explicit domain contracts. No product may import another product's private database tables, business implementation classes, or UI internals. All cross-product interactions occur through versioned APIs/events/contracts.

Work with **synthetic data only** during development. Never store real patient identifiers in prompts, logs, traces, or test fixtures. Do not allow AI-generated clinical content to be finalized without authorized human review. No autonomous diagnosis, treatment recommendation, prescribing, or medical-device claims in initial releases.

At the end of every milestone: show working functionality, tests run with results, security issues, known gaps, architecture changes, and exact next steps. Stop and request approval.

## 1. PRODUCT PORTFOLIO AND BOUNDARIES

### 1.1 Shared platform: HealthOS Core
- Identity and access management, OIDC/SSO/MFA, organization hierarchy and tenant isolation.
- Organization onboarding, localization, country/region configuration.
- Configuration Studio: industry templates, specialty profiles, dynamic forms, workflow rules, feature flags, dependency validation, versioning, preview, rollback.
- License and entitlements service: standalone, bundle, per-location, per-seat, usage-based AI entitlements; no hard-coded product assumptions.
- Audit trail, immutable audit events where appropriate, document/object storage, notifications, workflow orchestration, background jobs, observability, AI gateway, integration SDK.
- Platform admin console, tenant admin console, operational dashboard, backup/restore tooling.
- Core does **not** contain domain-specific clinical, dental, pharma, or medical equipment business logic.

### 1.2 Commercial product A: CareOps Quality AI
Standalone quality and documentation platform for healthcare, pharma, biotechnology, and medical device organizations.
- Controlled documents: drafting, templates, review, approval, publishing, revision, supersession, archive, effective dates, distribution.
- SOP authoring with AI assistance and source provenance; export to DOCX/PDF.
- Quality workflows: deviations, CAPA, change control, audits, training assignments, acknowledgment and evidence, configurable approval paths.
- AI knowledge assistant using permission-aware RAG and citations, document comparison, draft training materials, quality insights.
- Domain-specific regulatory profiles and record retention configurations, not claims of out-of-box regulatory certification.
- Must run without ClinicOS or HospitalOS and must not require patient records.

### 1.3 Commercial product B: ClinicOS AI
Standalone outpatient management platform, initially general clinics, with optional specialty packs.
- Patient registration and duplicate detection, appointments, practitioners, visits/encounters, histories, clinician-reviewed clinical notes, follow-up tasks, basic billing and document outputs.
- Consent and privacy controls; location-specific roles; configurable clinical forms and templates.
- AI documentation drafts, administrative summaries, follow-up suggestions, knowledge assistance with guardrails.
- Optional specialty packs: **Dental** (odontogram, tooth/procedure charting, treatment stages, recall), **Vision** (laterality-aware examinations, visual acuity, IOP, refraction, procedures and follow-ups), and future specialties.
- ClinicOS must run without CareOps, HospitalOS or MedTechOS.

### 1.4 Commercial product C: HospitalOS AI (future complete HMS)
- Reuse core patient/encounter contracts and outpatient workflows through supported interfaces; do not copy ClinicOS internal tables.
- Admission/discharge/transfer, inpatient beds and wards, nursing, medication administration, orders, pharmacy, laboratory, radiology, operating theatre, billing, insurance, inventory, reporting, multi-site hospital operations.
- Each hospital department is itself a modular capability with declared dependencies.
- Offer a complete HMS bundle or separately licensed modules where safe and coherent.
- Must support graceful operation if optional AI is unavailable.

### 1.5 Commercial product D: MedTechOS AI (later)
- Equipment registry, asset lifecycle, warranty, calibration, preventive maintenance, field service, service contracts, spare parts, controlled manuals and service intelligence.
- Standalone product; optional integrations to hospital assets and CareOps quality records through contracts.

### 1.6 HealthConnect (shared integration capability; optional commercial add-on)
- External HMS/EHR/ERP/QMS/CMMS integrations, REST/OpenAPI, HL7 FHIR where appropriate, HL7 v2 adapters where required, DICOM/DICOMweb where appropriate.
- Connector SDK, authentication vault, mapping studio, external identifiers, source-of-truth policies, retry/dead-letter queues, idempotency, reconciliation, connector health.
- AI can *suggest* field mappings; humans approve mapping and test results before activation.
- No promise of universal plug-and-play: each vendor's access/API rights and technical compatibility must be validated.

### 1.7 Shared AI services
- Model/provider abstraction with configurable approved providers, token budgets, usage metering and fallback.
- Permission-aware retrieval, document parsing, provenance/citations, structured-output schema validation.
- Human review queues, prompt/model versioning, AI audit trails, offline evaluation harness, red-team testing, hallucination/grounding tests, refusal/uncertainty behavior.
- AI Configuration Copilot: propose module selections, roles, forms and workflow configurations from natural language; generate a **draft diff**, validate dependencies and security, require administrator approval before publish.
- AI Workflow Copilot: summarize pending tasks, draft actions, flag administrative delays; no unsupervised clinically consequential action.
- Avoid AI-based diagnostic/therapeutic features until intended-use, risk, validation and regulatory pathways are explicitly approved.

## 2. COMMERCIAL COMPOSITION MODEL

Implement these supported deployment patterns:
1. **CareOps only:** Core + CareOps, no patient module.
2. **ClinicOS only:** Core + ClinicOS, no CareOps.
3. **Dental clinic:** Core + ClinicOS + Dental pack.
4. **Eye clinic:** Core + ClinicOS + Vision pack.
5. **Hospital bundle:** Core + HospitalOS + selected outpatient, lab, pharmacy, billing, quality, and AI modules.
6. **AI add-on only:** Core integration/AI services + HealthConnect, connected to an existing authorized system; no replacement EHR required.
7. **Pharma:** Core + CareOps, with GxP-oriented workflows and validated deployment requirements where applicable.
8. **MedTech:** Core + MedTechOS, optionally CareOps.

Support independent product branding, pricing plans, contracts, entitlements, feature flags, database migrations, release versions, deployment packages, backups, and customer documentation. The same platform code can underpin multiple installations. Modules must not be loaded if their dependencies or licenses are missing.

## 3. MODULAR ARCHITECTURE REQUIREMENTS

### 3.1 Initial architectural style
- Modular monolith for core business APIs with strictly enforced boundaries; separate Python AI worker/service and asynchronous integration workers.
- Use module manifests declaring: id, version, required core version, capabilities, dependencies, permissions, API/event contracts, migrations, UI routes, configuration schema, license requirements, health checks, install/upgrade/uninstall hooks.
- Avoid runtime execution of arbitrary third-party code in the initial plugin system. Initially plugins are **trusted, build-time packaged modules** with runtime enablement and versioned configuration. Document a future sandboxing model for externally authored plugins.
- Explicitly design data ownership, schema per module or equivalent enforced boundary, contract tests, backward-compatible schema migration, outbox/inbox for cross-module events.
- No cross-module direct SQL joins in production business logic. Read models may aggregate through approved events/APIs.
- Module disable must preserve required records, protect legal retention, and prevent destructive uninstall by default.

### 3.2 Proposed stack (confirm with user before irreversible choices)
- Frontend: React + TypeScript + Vite + Tailwind CSS, accessible responsive UI.
- Backend: ASP.NET Core (.NET current supported LTS), REST/OpenAPI, domain-driven modules.
- AI: Python FastAPI service with worker queue, provider-agnostic model interface.
- Persistence: PostgreSQL, pgvector where justified, S3-compatible object storage.
- Messaging/jobs: initially durable outbox + background worker; introduce broker/Redis only where demonstrated need exists.
- Identity: standards-based OIDC, MFA, RBAC/ABAC and enterprise SSO options.
- Deployment: containerized services, IaC, CI/CD, secret manager, OpenTelemetry; avoid mandatory Kubernetes initially.
- Testing: xUnit or equivalent for .NET, pytest for Python, Playwright for end-to-end UI, API contract tests, load/security tests.
- Use dependency pinning, SBOM, vulnerability scans, migration tests and deterministic builds.

### 3.3 Suggested repository
```
healthos/
  README.md
  CLAUDE.md
  docs/
    product/ architecture/ adrs/ api/ security/ compliance/ runbooks/ decisions/
  apps/
    admin-web/
    clinic-web/
    quality-web/
    hospital-web/                # later
  services/
    core-api/
    ai-service/
    integration-worker/
  modules/
    core-identity/
    core-tenancy/
    core-configuration/
    core-licensing/
    core-audit/
    core-documents/
    core-workflows/
    core-notifications/
    careops-quality/
    clinicos-core/
    specialty-dental/
    specialty-vision/
    hospitalos/                  # later
    medtechos/                   # later
  contracts/
    api/
    events/
    fhir-mappings/
  packages/
    ui-kit/
    module-sdk/
    shared-types/
  deploy/
    local/
    staging/
    production-examples/
  tests/
    architecture/
    contracts/
    integration/
    e2e/
    security/
    performance/
    ai-evaluations/
  scripts/
```
Adjust actual structure if justified by ADR. Do not create empty folders to imply functionality exists.

## 4. CONFIGURATION STUDIO — FIRST-CLASS PRODUCT

- Hierarchy: global defaults -> country/regulatory profile -> industry profile -> specialty -> organization -> site/department -> role/user, with explicit precedence and inheritance.
- Each setting has type, schema, validation, allowed scope, effective date, owner, audit record, migration behavior and rollback plan.
- Template examples: general clinic, dental clinic, ophthalmology clinic, 20-bed hospital, multi-specialty hospital, pharma quality team, medical equipment service provider.
- Module catalog with dependencies, entitlements, required permissions, required data contracts, incompatible combinations and suggested bundles.
- Configurable forms: typed fields, units, terminology, required fields, conditional display, permissions, validation and versioning. No arbitrary unreviewed executable code in form rules.
- Workflow designer: states, transitions, role approvals, SLAs, escalation, notifications, retries, exception handling and human review.
- Configuration Copilot generates recommendations only; validate and show change preview; require authorized publish; allow rollback; audit all changes.
- Localization: India and EU regions, date/time, currency, languages, consent/retention and data-processing configurations. Do not assume one EU country equals all EU jurisdictions.
- Provide a tenant onboarding wizard and admin preview of activated product.

## 5. DATA AND INTEROPERABILITY

- Separate domain concepts for organization, site, practitioner, patient, encounter, observation, appointment, order, document, quality record, equipment, integration endpoint.
- Use stable internal IDs and explicit external IDs with source-system ownership.
- Map suitable clinical resources to HL7 FHIR (e.g. Patient, Practitioner, Organization, Encounter, Appointment, Observation, DocumentReference) using tested mappings; do not claim full FHIR conformance without testing.
- Consent, purpose limitation, retention, right-to-access/deletion workflows subject to legal record-retention exceptions.
- Tenant isolation enforced at database query and API layers, not merely UI filtering. Test cross-tenant denial systematically.
- Encryption in transit/at rest, field-level protection where needed, backups and restore tests, access/audit logs with redaction.
- For add-ons define authoritative source, sync direction, conflict policy, retries, idempotency keys, reconciliation and outage behavior.
- No patient PHI in telemetry, general-purpose model prompts or external integrations without lawful basis and approved safeguards.

## 6. SECURITY, PRIVACY, QUALITY AND REGULATORY GATES

- Threat model per product and cross-tenant boundaries, secure SDLC, MFA, least privilege, service-to-service auth, secret rotation, rate limits, input validation, malware scanning for uploads, audit log integrity, dependency scanning.
- EU: GDPR obligations, appropriate data processing agreements, transfer assessments and mechanisms, DPIA when applicable, role allocation, breach response; medical-device assessment under MDR/IVDR and applicable AI rules based on intended use; EHDS applicability where relevant.
- India: applicable DPDP provisions and commencement, ABDM standards where integrated, applicable health and medical-device regulations.
- Pharma: GxP applicability, validation lifecycle, electronic records/signatures, EU GMP Annex 11 and 21 CFR Part 11 as applicable; do not label software compliant by default.
- MedTech: determine intended use and applicable ISO 13485, IEC 62304, ISO 14971, and jurisdiction-specific requirements when relevant.
- Establish intended-use statement, risk classification, clinical safety assessment and human oversight per AI feature. Medical diagnosis, triage, medication decisions and image interpretation are excluded from initial release until separately authorized.
- Create compliance evidence matrix and risk register, not marketing claims of certification.
- Product should retain essential functionality when AI or external integration is unavailable.

## 7. AI PRODUCT DESIGN AND EVALUATION

Implement AI capabilities as opt-in, licensed, policy-controlled modules:
1. Configuration Copilot (industry profile suggestions, forms/workflows draft).
2. SOP Copilot (draft from approved sources with citations and review).
3. Quality Copilot (document comparisons, deviations/CAPA draft, training draft).
4. Knowledge Copilot (permission-filtered RAG with source passages and freshness).
5. Clinical Documentation Copilot (draft notes only; clinician attestation).
6. Administrative Workflow Copilot (summaries, reminders, exceptions).
7. Integration Mapping Copilot (mapping proposals, sample-data validation).
8. Service Copilot for MedTech (manual lookup and draft service reports).

For every capability: define allowed input, prohibited input, data residency, output schema, source provenance, model version, approval gate, evaluation dataset, acceptance thresholds, monitoring and fallback. Treat prompt injection in retrieved documents as a threat. Never let an LLM directly execute SQL, publish configurations, prescribe medication or perform irreversible operations without authorization.

## 8. UX AND CUSTOMER ONBOARDING

- One polished design system with optional product-specific branding, accessible components, responsive layouts, keyboard support, consistent validation/errors.
- Admin flow: create tenant -> select country -> choose industry -> choose specialty -> select modules/bundle -> assign roles -> configure forms/workflows -> preview -> approve -> activate.
- User home screen shows only entitled modules, permitted data and relevant tasks.
- Provide demo data for: general clinic, dental clinic, eye clinic, pharma QA, medical equipment service, hospital admin.
- Show module installation status, dependency errors, version, license, configuration history and connector health.
- No placeholder buttons, fake metrics, or hard-coded sample data presented as real production activity.

## 9. MILESTONES — BUILD IN THIS ORDER

### M0 — Discovery, ADRs and executable plan
Produce `docs/product/vision.md`, `docs/product/module-catalog.md`, `docs/product/acceptance-criteria.md`, `docs/architecture/system-context.md`, `docs/architecture/module-boundaries.md`, `docs/architecture/data-ownership.md`, `docs/architecture/deployment-models.md`, `docs/security/threat-model.md`, `docs/compliance/regulatory-scope.md`, ADRs for stack/tenancy/plugins/integration/AI, and a prioritized implementation backlog. Build no speculative features. Ask for approval.

### M1 — Engineering foundation
Create working monorepo, CI, local development scripts, health checks, PostgreSQL migrations, OIDC dev auth, core API and web shell, secure configuration and automated baseline tests. Include reproducible setup commands.
**Gate:** fresh checkout starts successfully; tests and scans pass.

### M2 — Core identity, tenancy, audit and licensing
Implement tenant/site/org hierarchy, users/roles/permissions, audit events, entitlements, product catalog, access enforcement, tenant isolation tests.
**Gate:** prove tenant A cannot read/write tenant B data; unauthorized modules cannot load.

### M3 — Module SDK and Configuration Studio
Build module manifests, dependency resolution, install/enable/disable, version compatibility, configuration schema, inheritance, preview/publish/rollback, admin wizard, demo templates.
**Gate:** instantiate five distinct customer profiles without code edits; each loads correct modules, forms and permissions; disabling a module preserves records.

### M4 — Shared document/workflow/AI services
Implement document storage/versioning, workflow state engine, notifications, AI gateway, human approval queues, permission-aware RAG with citations, AI usage monitoring and evaluations.
**Gate:** AI draft cannot auto-publish; citations resolve; permissions enforced; service outage fallback works.

### M5 — CareOps Quality AI standalone v1
Complete SOP authoring -> review -> approval -> publish -> revision; training assignment/acknowledgment; basic deviation and CAPA; knowledge assistant; reporting.
**Gate:** deploy **Core + CareOps only** with no ClinicOS dependency; full end-to-end workflow tested and demoed.

### M6 — ClinicOS AI standalone v1
Complete registration -> appointment -> encounter -> clinician-approved note -> follow-up -> billing/receipt flow using synthetic data. Include consent, clinical audit and basic reports.
**Gate:** deploy **Core + ClinicOS only** with no CareOps dependency; safety and data integrity tests pass.

### M7 — Dental and Vision specialty packs
Dental odontogram, tooth-level treatments and recall; Vision laterality-aware examination templates, measurements and procedure follow-up; both configurable.
**Gate:** activate/deactivate each pack in separate clinics without code changes; core ClinicOS continues functioning.

### M8 — HealthConnect integration platform
Implement SDK, API auth, mapping, FHIR test endpoints, durable sync, idempotency, dead-letter queue, reconciliation, source-of-truth policies, connector dashboard. Build simulated HMS connector; then one real connector only with documented permission/access.
**Gate:** mock connector recovers from outage without duplication; cross-system data ownership respected.

### M9 — Bundling and commercial packaging
Demonstrate standalone CareOps, standalone ClinicOS, Dental bundle, Vision bundle, CareOps+ClinicOS, AI add-on only, and future HospitalOS assembly. Implement entitlements, installation bundles, upgrade and rollback testing.
**Gate:** all listed bundles pass isolated deployment and contract tests.

### M10 — HospitalOS modules (subsequent release trains)
Implement OPD integration, ADT, bed/ward, nursing, orders, pharmacy, lab, radiology, billing, discharge, inventory and reporting in individually validated increments. Require clinical advisors and department-level pilots. No claim of complete HMS until actual end-to-end hospital workflows are validated.

### M11 — MedTechOS (separate release train)
Equipment, service, calibration, warranties, preventive maintenance and AI service knowledge. Reuse Core but no hard dependency on hospital or clinic products.

### M12 — Production readiness per product
Independent penetration testing, privacy/security assessment, backup restore, DR drills, performance/load tests, accessibility, AI evaluation, customer UAT, regulatory review where relevant, monitoring, support/runbooks, release/rollback. No production patient data until gates are approved.

## 10. REQUIRED AUTOMATED TESTS

- Module boundary tests forbid cross-product internal imports and database access.
- Contract tests for public APIs/events and supported version compatibility.
- Tenant isolation and RBAC/ABAC matrix tests.
- Entitlement tests for standalone/bundle/add-on deployments.
- Configuration inheritance, validation, dependency, rollback and migration tests.
- Cross-tenant RAG access denial and prompt-injection tests.
- Clinical draft human-review gate tests.
- Sync retry, idempotency, outage recovery and reconciliation tests.
- Database migration forward/backward compatibility tests where supported.
- Backup/restore, security scanning, API fuzzing, E2E user journeys, accessibility and load testing.
- Tests must run in CI with visible reports; do not mark tests passed unless executed.

## 11. DOCUMENTATION AND OUTPUT EXPECTATIONS

Maintain:
- `CLAUDE.md`: repository rules, commands, module boundaries, review gates.
- `docs/product/`: personas, PRD, workflow specs, user journeys, pricing/packaging hypotheses.
- `docs/architecture/`: C4 diagrams (Mermaid), ADRs, service contracts, event schemas, data ownership.
- `docs/security/`: threat model, data flows, access matrix, incident/backup/DR runbooks.
- `docs/compliance/`: intended use and regulatory applicability by module and country; validation evidence placeholders.
- `docs/ai/`: model policy, evaluation suites, prompt versions, provenance, cost controls.
- `docs/operations/`: local/staging setup, monitoring, release and rollback.
- `docs/testing/`: test matrix and reproducible results.
- `docs/milestones/`: completion reports with actual commands, results, unresolved issues.

## 12. FIRST CLAUDE CODE SESSION — EXECUTE NOW

1. Inspect current repository, installed runtimes, tooling and existing source. Do not overwrite user files.
2. Restate the architecture in a short decision summary, identifying uncertainties and dangerous assumptions.
3. Create M0 documents and architecture diagrams; explicitly specify module manifests, entitlements, tenant data isolation, configuration precedence and cross-product contracts.
4. Produce a backlog of small, independently testable tickets for M1–M3 and rough effort/risk estimates.
5. Recommend the simplest viable local developer environment, with commands for Windows/VS Code and CI.
6. Present a milestone acceptance checklist and stop for approval **before implementing M1**.

**Success means:** one maintainable platform that supports standalone products, optional combinations and a future complete HMS without coupling products to each other. A polished UI without secure workflows, tests and verifiable modularity is not success.
