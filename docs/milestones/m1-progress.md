# M1 Report — Engineering foundation

**Status:** ✅ Complete (2026-11-19), verified in CI. Remaining gap: the owner's PC cannot run .NET code locally (Windows Smart App Control), so the "fresh checkout on the owner's machine" timing is not done; the equivalent fresh-checkout run happens on every CI e2e job.

| Gate (spec §9 M1) | Evidence | Result |
|---|---|---|
| Fresh checkout starts successfully | CI `e2e` job: clean runner, `docker compose up --wait`, `db bootstrap` + `db migrate` (twice), core-api + admin-web start, readiness OK | ✅ |
| Login through OIDC with MFA-capable IdP | Playwright: Chromium → admin-web → BFF → Keycloak → back; HttpOnly session; user provisioned | ✅ |
| Tests and scans pass | dotnet (unit, host, architecture, DB integration), ai-service (ruff, mypy strict, pytest, pip-audit), web (typecheck, vitest + axe, build, audit), gitleaks, SBOM | ✅ |
| Module boundaries enforced | Architecture tests with deliberate-violation fixtures | ✅ |

## Tickets
M1-01 … M1-15 delivered (see git history and `docs/milestones/backlog-m1-m3.md`). Notable changes from plan: Python 3.14, Microsoft Testing Platform, SQL-first migrations (ADR-0006), no object storage until M4.

## Bugs found by CI and fixed during M1
mypy ignore for pydantic-settings; configuration not loaded from other working directories; anonymous API calls redirected to the IdP instead of 401; dev proxy Host header; TypeScript 7 native compiler install on the dev PC (pinned 5.9).

## Carried forward
- Bump GitHub Actions off Node 20.
- Local development on the owner's PC requires turning off Smart App Control and installing WSL2 (see `docs/operations/local-dev-environment.md`).
