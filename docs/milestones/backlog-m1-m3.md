# Backlog — M1 to M3

Effort: **S** ≤ 1 day · **M** 2–3 days · **L** 4–5 days (one experienced engineer + AI assistance). Risk: **L/M/H** = chance of surprise or rework. Each ticket is independently testable; "Done" = acceptance tests executed and passing in CI.

## M1 — Engineering foundation (≈ 4–5 weeks)

| ID | Ticket | Acceptance | Effort | Risk |
|---|---|---|---|---|
| M1-01 | Git repo, `.gitignore`, `.editorconfig`, `.gitattributes`, licence header policy | Repo initialised; secrets patterns ignored | S | L |
| M1-02 | .NET solution skeleton: `services/core-api` host, `Directory.Build.props`, `Directory.Packages.props` (central pinning), nullable + warnings-as-errors | `dotnet build` clean | S | L |
| M1-03 | Module SDK v0 package: `IModule` (register services, map endpoints, health checks), manifest model + JSON Schema | Host discovers a sample module from package list | M | M |
| M1-04 | Architecture test project: module reference rules, "every endpoint declares a permission" rule | Deliberate-violation fixture fails the test | M | M |
| M1-05 | Docker Compose: PostgreSQL 17 + pgvector, Keycloak, SeaweedFS, OTel collector + Jaeger, Mailpit | `scripts/dev-up.ps1` brings all healthy | M | M |
| M1-06 | Migration runner: per-module EF migrations into own schema, migration role vs runtime role, bootstrap SQL creating roles | Fresh DB migrates; re-run is a no-op | M | M |
| M1-07 | Health endpoints `/health/live`, `/health/ready` (DB, storage) | Ready = 503 when Postgres down (test) | S | L |
| M1-08 | Keycloak realm export (dev), synthetic users, BFF login flow in core-api (cookie session, PKCE) | Login from admin-web works; `/api/me` returns identity | L | H |
| M1-09 | Structured logging + OpenTelemetry tracing with PHI redaction enricher | Canary test: synthetic PHI field never appears in log output | M | M |
| M1-10 | Secure config: options validation, user-secrets for dev, no secrets in repo, gitleaks in CI | Startup fails fast on missing config; gitleaks passes | S | L |
| M1-11 | ai-service skeleton: FastAPI, uv, health endpoints, fake provider, signed-context verification | pytest green; rejects unsigned calls | M | L |
| M1-12 | Frontend workspace: pnpm, `packages/ui-kit` (tokens, Button, Input, Dialog, Table, Toast; Radix), `apps/admin-web` shell with login, nav from API manifest | Vitest + axe accessibility checks pass; Playwright login smoke test | L | M |
| M1-13 | CI pipeline: build/test .NET, Python, web; arch tests; dependency scans; SBOM; Playwright smoke against Compose | Pipeline green on PR; fails on injected high CVE | L | M |
| M1-14 | `docs/operations/local-dev-environment.md` verified on clean machine; `docs/compliance/evidence-matrix.md` + `risk-register.md` seeded | Timed fresh-checkout run recorded | S | L |
| M1-15 | M1 milestone report | Report with commands + outputs | S | L |

## M2 — Identity, tenancy, audit, licensing (≈ 5–6 weeks)

| ID | Ticket | Acceptance | Effort | Risk |
|---|---|---|---|---|
| M2-01 | `core.tenancy`: tenant/org/site/department model, region cell field, CRUD API for platform admin | API + tests | M | L |
| M2-02 | Tenant context pipeline: token claim → `ITenantContext` → DB interceptor `SET LOCAL app.tenant_id`; fail-closed | Missing context → 0 rows / insert error | M | H |
| M2-03 | RLS policy template + arch/DB test that every table with `tenant_id` has FORCED RLS | Test detects a table without policy | M | M |
| M2-04 | Cross-tenant test harness: generic matrix runner over all endpoints (read/write/delete as tenant B) | Runs in CI; report artifact | L | M |
| M2-05 | `core.identity`: users (linked to IdP subject), roles, permissions catalog from manifests, role assignments with site/department scope | API + tests | L | M |
| M2-06 | Authorization: permission policies per endpoint, ABAC scope checks, deny-by-default | RBAC matrix test | M | M |
| M2-07 | MFA enforcement policy per tenant/role (via Keycloak ACR/AMR claims) | Admin endpoints reject non-MFA session | M | H |
| M2-08 | `core.audit`: append-only table (no UPDATE/DELETE grants), per-tenant hash chain, audit API, redaction | Tamper detection test | M | M |
| M2-09 | `core.licensing`: products, features, plans, subscriptions, entitlements, usage meters; operator API | Unit + API tests | L | M |
| M2-10 | Entitlement gate: route-level middleware + navigation manifest filtering + job/event handler guard | Unentitled module → 404 and absent from nav | M | M |
| M2-11 | Expiry/grace/read-only behavior | Tests for each state | M | M |
| M2-12 | Admin-web: tenants, users/roles, entitlements screens (platform + tenant admin) | Playwright journeys | L | M |
| M2-13 | Platform break-glass access (time-boxed, audited) | Audit + expiry test | M | M |
| M2-14 | M2 milestone report incl. isolation evidence | — | S | L |

## M3 — Module SDK and Configuration Studio (≈ 6–7 weeks)

| ID | Ticket | Acceptance | Effort | Risk |
|---|---|---|---|---|
| M3-01 | Manifest schema v1 finalised + CI validation of all manifests | Invalid manifest fails CI | S | L |
| M3-02 | Dependency resolver (semver, cycles, incompatibilities, entitlements, region policy) with explainable errors | Property-based + table tests | M | M |
| M3-03 | Module lifecycle per tenant: enable/disable/uninstall hooks; non-destructive disable | Disable preserves records test | M | M |
| M3-04 | Bundle build profiles + CI matrix building minimal packages; package-inspection test | CareOps-only image has no ClinicOS assemblies (with stub modules) | M | M |
| M3-05 | Settings registry from module schemas; typed values; scopes | Unit tests | M | L |
| M3-06 | Precedence resolver (8 levels, locks, constraints, multi-role merge, effective dating, provenance) | Exhaustive test suite | L | H |
| M3-07 | Draft → validate → preview → approve → publish → rollback lifecycle with immutable snapshots + audit | Lifecycle tests | L | M |
| M3-08 | Form definition model v1 (typed fields, units, required, conditional display via declarative expressions, permissions, versioning) — no executable code | Validation tests; expression sandbox tests | L | H |
| M3-09 | Workflow definition model v1 (states, transitions, role approvals, SLA metadata) — definitions only; engine is M4 | Schema + validation tests | M | M |
| M3-10 | Industry/specialty templates: general clinic, dental, eye clinic, pharma QA, equipment service (using stub product modules) | Five profiles instantiate without code edits (snapshot tests) | M | M |
| M3-11 | Onboarding wizard in admin-web (tenant → country → industry → specialty → modules → roles → forms/workflows → preview → approve → activate) | Playwright E2E for 2 profiles | L | M |
| M3-12 | Admin views: module status, dependency errors, versions, licence, configuration history with diff | Playwright | M | L |
| M3-13 | Localization basics: IN and EU-DE/EU-FR profiles (date, currency, language packs en/hi/de) | Tests | M | L |
| M3-14 | M3 milestone report | — | S | L |

**Note on stub modules in M3:** M3 gates need product modules to exist for profiles to load. We'll use minimal *real* module skeletons (manifest, schema, one read endpoint, permissions) for careops.quality, clinicos.core, specialty.dental, specialty.vision, medtechos.assets. They're clearly marked as skeletons in the UI and docs, which keeps us within the spec's "no fake functionality" rule.

## Rough total
M1–M3 ≈ **15–18 engineer-weeks** for one senior engineer with AI assistance; biggest risks: auth/BFF + MFA integration (M1-08, M2-07), tenant context correctness (M2-02), precedence resolver and form expression safety (M3-06, M3-08).
