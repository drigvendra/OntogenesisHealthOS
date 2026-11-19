# M1 Progress Report — Engineering foundation (in progress)

**Last updated:** 2026-11-18 (session 2) · **Status:** Code for all M1 tickets except the Playwright smoke test is written. **Verification is blocked on the owner's machine** (Smart App Control, WSL2). M1 gate not met.

## Ticket status
| Ticket | Status | Evidence |
|---|---|---|
| M1-01 Repo + base config | ✅ Done | git initialised (no commits yet), ignore/attributes/editorconfig, `.env.example` |
| M1-02 .NET solution skeleton | ✅ Done | `HealthOS.slnx`, `global.json`, warnings-as-errors, NuGet audit, lock files, central versions, no unsigned apphost .exe |
| M1-03 Module SDK v0 | ✅ Verified | 30/30 tests |
| M1-04 Architecture tests | ✅ Verified | 4/4 tests, including deliberate-violation fixtures |
| M1-05 Compose stack | ⚠ Written, not run | Postgres (host port 5433), Keycloak, Jaeger, Mailpit |
| M1-06 Migrations + DB roles | 🟡 Code complete, **not run** | `HealthOS.Platform.Data`: bootstrap (database, `hos_migrator`, `hos_app` NOINHERIT), per-module schema + NOLOGIN role + default privileges, EF migrations per module, `SET ROLE` interceptor. `core-api db bootstrap` / `db migrate` commands. First table `core_identity.user_account` (hand-written migration). 9 integration tests (idempotency, model/migration match, role privilege checks, cross-schema denial, provisioning). Run in CI against `postgres:17`; blocked locally. |
| M1-07 Health endpoints | 🟡 Code done, tests blocked | — |
| M1-08 OIDC BFF login | 🟡 Code done, not verified | Includes just-in-time user provisioning (internal user id claim `hos_uid`) |
| M1-09 Logging/tracing + redaction | ✅ Verified (canary) | — |
| M1-10 Secure configuration | 🟡 Code done, tests blocked | — |
| M1-11 ai-service | ✅ Verified | 15/15, ruff clean |
| M1-12 ui-kit + admin-web | ✅ Verified (unit) | 4/4 incl. axe; typecheck; build |
| M1-13 CI | ⚠ Written, not run | Now includes the PostgreSQL service for integration tests |
| M1-14 Docs, evidence matrix, risk register | ✅ Done (fresh-checkout timing pending) | `docs/compliance/evidence-matrix.md`, `docs/compliance/risk-register.md` |
| M1-15 Final M1 report | Pending verification | — |

## What Windows blocks on this machine (Code Integrity log)
`HealthOS.CoreApi.dll`, `HealthOS.CoreApi.Tests.dll`, `HealthOS.IntegrationTests.exe`, `HealthOS.Platform.Data.dll`, `HealthOS.Core.Identity.Contracts.dll`, uv-generated `.exe` shims, mypy compiled modules. The core-api **cannot run at all** locally while Smart App Control is on.

Attempted workarounds: no apphost `.exe` (done; DLLs still blocked); signed Python interpreter (works); native PostgreSQL install (declined by the permission system as a system-wide install; a PostgreSQL 16 service already on port 5432 was left untouched).

## Tests executed this session
| Suite | Result |
|---|---|
| Module SDK | 30 passed |
| Architecture | 4 passed |
| core-api host (15), integration (9) | Not run — blocked by Smart App Control |
| ai-service | 15 passed (previous run; unchanged) |
| web | 4 passed (previous run; unchanged) |

## To close M1
1. Owner: turn off Smart App Control **or** push the repo to GitHub so CI can run everything; install WSL2 for the local stack.
2. Run `./scripts/dev-up.ps1`, `./scripts/test.ps1` and the CI pipeline; fix whatever fails (expect small issues: image tags, Keycloak realm import, hand-written migration).
3. Manual login check with Keycloak + TOTP; Playwright login smoke test.
4. Timed fresh-checkout run; final M1 report; owner approval for M2.
