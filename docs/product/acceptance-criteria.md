# Acceptance Criteria by Milestone

Each criterion must be demonstrated by an **executed** automated test or a recorded manual demo script, referenced in the milestone report. "Passes" means it ran in CI or locally in this repo and the output is attached.

## M0 — Discovery, ADRs, plan
- [ ] All M0 documents listed in spec §9 exist and are internally consistent.
- [ ] ADRs for stack, tenancy, plugins, integration and AI exist with status Proposed.
- [ ] Backlog for M1–M3 has small tickets with acceptance, effort and risk.
- [ ] Owner has approved or amended: stack, tenancy model, clinical.registry split, first product order.

## M1 — Engineering foundation
- [ ] `git clone` + documented commands on a clean Windows machine bring up Postgres, IdP, core-api, ai-service and admin-web in ≤ 15 minutes (timed, recorded).
- [ ] `/health/live` and `/health/ready` on core-api and ai-service; ready fails when Postgres is down.
- [ ] A user can log in to admin-web through the dev OIDC provider (MFA can be enabled) and see their identity from the API.
- [ ] EF Core migrations apply to an empty DB and are idempotent on re-run.
- [ ] CI runs build, unit tests, architecture tests, lint, dependency vulnerability scan, secret scan and SBOM generation; any high/critical vulnerability fails the build.
- [ ] Architecture test fails if a module references another module's internal project (demonstrated with a deliberate violation in a test fixture).

## M2 — Identity, tenancy, audit, licensing
- [ ] For every tenant-owned table: automated test that tenant A cannot read, update, or delete tenant B rows via API **and** via direct DB session with `app.tenant_id` = A (RLS).
- [ ] Missing `app.tenant_id` on a session returns zero rows (fail closed).
- [ ] RBAC matrix test: every API endpoint is declared with a permission; an endpoint without a permission declaration fails the architecture test.
- [ ] Audit events are written for login, role change, entitlement change and data access on audited resources; hash chain verification detects a tampered row.
- [ ] A module whose entitlement is absent or expired is not routable for that tenant (404/403) and its UI routes are not returned in the navigation manifest.

## M3 — Module SDK and Configuration Studio
- [ ] Five customer profiles (general clinic, dental clinic, eye clinic, pharma QA, equipment service) are instantiated from templates with **no code edits**; each loads exactly the expected modules, forms and roles (snapshot test).
- [ ] Dependency resolver rejects: missing dependency, version incompatibility, cycle, incompatible combination, missing entitlement, with a human-readable error.
- [ ] Config precedence test suite covers all 8 levels, locks, and constraint validation.
- [ ] Publish creates an immutable snapshot; rollback restores the previous snapshot; both are audited.
- [ ] Disabling a module hides it but preserves all its records; re-enabling restores access; uninstall with retained records is refused by default.

## M4 — Documents, workflows, AI services
- [ ] An AI draft cannot transition to published/final without a human approval event (domain test + API test with a service principal attempting it).
- [ ] Every citation in a RAG answer resolves to a document version the requesting user can read; cross-tenant and cross-permission retrieval tests return nothing.
- [ ] Prompt-injection test corpus: retrieved documents containing instructions don't change tool use or permission scope.
- [ ] With ai-service stopped, all non-AI workflows complete and AI features show "unavailable" without errors.
- [ ] AI usage is metered per tenant and capability; exceeding budget blocks further calls.

## M5 — CareOps standalone
- [ ] Build artifact for **Core + CareOps** contains no ClinicOS/HospitalOS assemblies (verified by package inspection test).
- [ ] E2E (Playwright): SOP draft → review → approval (e-signature with re-auth) → publish → training assignment → acknowledgment → revision → supersession.
- [ ] Deviation → CAPA → effectiveness check E2E.

## M6 — ClinicOS standalone
- [ ] Build artifact for **Core + ClinicOS** contains no CareOps assemblies.
- [ ] E2E: registration (duplicate detection) → appointment → encounter → AI draft note → clinician attestation → follow-up → bill/receipt, synthetic data only.
- [ ] Consent withdrawal blocks the relevant processing and is audited.

## M7–M12
Gates as defined in spec §9; detailed criteria will be written at the start of each milestone.
