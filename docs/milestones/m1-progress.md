# M1 Progress Report — Engineering foundation (in progress)

**Last updated:** 2026-11-19 · **Repository:** https://github.com/drigvendra/OntogenesisHealthOS · **CI:** GitHub Actions (`.github/workflows/ci.yml`)

Status: all M1 code written. Everything except the browser login journey is **verified in CI**. The e2e login job is being debugged.

## Verified in CI (GitHub Actions, ubuntu-latest)
| Job | What runs | Result |
|---|---|---|
| dotnet | Locked restore, Release build (warnings as errors, NuGet audit), 64 tests: Module SDK 30, architecture 4, core-api host 21, **database integration 9 against PostgreSQL 17** | **64/64 passed, 0 skipped** |
| ai-service | ruff, **mypy strict**, pytest (15), pip-audit | passed |
| web | typecheck, vitest + axe (4), production build, pnpm audit | passed |
| security | gitleaks secret scan, CycloneDX SBOM | passed |
| e2e | compose stack healthy (Postgres, Keycloak, Jaeger, Mailpit); `db bootstrap` + `db migrate` run **twice** (idempotent); core-api starts and is ready | passed up to the Playwright step |
| e2e — Playwright login journey | Browser → admin-web → BFF → Keycloak → back, HttpOnly session, provisioned user id | **failing, under investigation** |

## Bugs found by CI and fixed
1. `pyproject.toml` written with a UTF-8 BOM by PowerShell (local).
2. mypy `call-arg` ignore needed for pydantic-settings (I had removed it).
3. **core-api loaded no `appsettings.json` when started as `dotnet path/to/dll`** from another directory → content root now pinned to the app directory.
4. **Anonymous API calls were redirected (302) to Keycloak instead of returning 401** — a browser `fetch` would fail cross-origin and the SPA would show "unavailable". Challenge now goes to the cookie handler; OIDC only starts at `/bff/login`. Host tests changed to keep the production challenge behaviour, plus a regression test.
5. OIDC response mode switched to `query` with Lax correlation cookies (form_post needs SameSite=None + HTTPS).
6. TypeScript 7 (native compiler) install incomplete on the dev machine → pinned TypeScript 5.9.

## Ticket status
| Ticket | Status |
|---|---|
| M1-01 Repo | ✅ |
| M1-02 .NET skeleton | ✅ |
| M1-03 Module SDK | ✅ verified |
| M1-04 Architecture tests | ✅ verified |
| M1-05 Compose stack | ✅ verified in CI (all services healthy) |
| M1-06 Migrations + DB roles | ✅ verified in CI (integration tests + e2e bootstrap/migrate twice) |
| M1-07 Health endpoints | ✅ verified (host tests; e2e readiness) |
| M1-08 OIDC BFF login | 🟡 host-level verified; browser journey pending |
| M1-09 Logging/redaction | ✅ verified |
| M1-10 Secure configuration | ✅ verified (fail-fast tests; found and fixed content-root bug) |
| M1-11 ai-service | ✅ verified incl. mypy |
| M1-12 ui-kit + admin-web | ✅ verified; Playwright smoke pending (same as M1-08) |
| M1-13 CI | ✅ running on every push |
| M1-14 Docs, evidence matrix, risk register | ✅ (fresh-checkout timing on the owner's machine still blocked) |
| M1-15 Final report | after e2e passes |

## Owner's machine (unchanged)
Smart App Control blocks our compiled .NET assemblies and WSL2 is missing, so the stack can't run locally. CI is the source of truth until that changes. See `docs/operations/local-dev-environment.md`.

## Known gaps carried forward
- Logout from the SPA uses `fetch` + navigation; the Keycloak SSO session isn't ended by the browser (front-channel logout to be done properly in M2).
- GitHub Actions warn that several actions still target Node 20; bump action versions.
- Log redaction covers classified `[LoggerMessage]` calls only; analyzer pending.
