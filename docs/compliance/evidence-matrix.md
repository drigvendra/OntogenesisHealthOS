# Compliance & Control Evidence Matrix

Maps requirements to implemented controls and the evidence that they work. **Status is only "Verified" when the evidence was executed and its output recorded in a milestone report.** This is engineering evidence, not a certification claim.

| ID | Requirement (source) | Control | Evidence | Status |
|---|---|---|---|---|
| C-001 | Products independently composable; no cross-module internals (spec §0, §3.1) | Contracts-only references; manifest-declared dependencies | `tests/architecture` reference rules + violation fixtures | Verified (M1, 2026-11-18) |
| C-002 | Module dependencies/versions validated before load (spec §2, §3.1) | `PackageResolver` fails host startup | SDK tests (12 resolver cases); `ModuleHostTests` | Resolver verified; host test written, blocked locally |
| C-003 | No cross-module SQL access (spec §3.1) | Schema per module; NOINHERIT app login + per-module roles | `DatabaseIsolationTests` | Written; runs in CI only |
| C-004 | Destructive uninstall prevented by default (spec §3.1) | Manifest validator rejects `destructiveUninstall: true` | SDK test | Verified |
| C-005 | No PHI in logs (spec §0, §5) | Data classification + erasing redactor | `RedactionTests` canary | Verified (classified log calls only) |
| C-006 | Deny-by-default access (spec §6) | Every endpoint declares access; fallback policy requires auth; permission checks fail closed until M2 | `Every_endpoint_declares_exactly_one_access_rule`, `Permission_policies_deny_until_m2_grants_exist` | Written; blocked locally |
| C-007 | CSRF / open redirect protection (spec §6) | X-CSRF header on APIs; local-only return URLs | core-api host tests; admin-web test sends header | Web verified; host tests blocked locally |
| C-008 | AI cannot be called without authorization context (spec §7) | Signed short-lived context; capability scoping; drafts only | ai-service tests (15) | Verified |
| C-009 | Secrets not in repository (spec §6) | `.env` + user-secrets, generated randomly; gitleaks in CI | `.gitignore`; CI job | Partially: CI not yet run |
| C-010 | Dependency vulnerability management (spec §3.2) | NuGet audit (build fails), pnpm audit, pip-audit; documented exceptions | Build output; `dependency-exceptions.md` | Verified for .NET and web |
| C-011 | Accessibility (spec §8) | Landmarks, skip link, axe checks in tests | admin-web tests | Verified (unit level) |
| C-012 | Tenant isolation at DB layer (spec §5) | RLS FORCED + tenant context | — | Planned M2 |
| C-013 | Immutable audit trail (spec §1.1) | Hash-chained append-only audit | — | Planned M2 |
