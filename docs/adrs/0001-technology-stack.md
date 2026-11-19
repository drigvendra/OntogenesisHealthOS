# ADR-0001: Technology stack

- **Status:** Accepted (owner chose C#/.NET on 2026-11-18), with the amendments below
- **Date:** 2026-11-18

## Amendments made during M1 (2026-11-18)
| Item | Original proposal | Actual | Reason |
|---|---|---|---|
| Python | 3.13 via uv | **3.14 (python.org, PSF-signed build)** | Windows Smart App Control on the dev machine blocks uv's unsigned venv launcher; a venv from the signed interpreter works. Python 3.14 is a year old and well supported. |
| .NET tests | xUnit v3 + VSTest | **xUnit v3 on Microsoft Testing Platform** (`global.json` test runner) | .NET 10 SDK no longer runs xUnit v3 via VSTest. |
| Microsoft.Extensions.* packages | latest | **pinned to 10.x line** | Stay aligned with .NET 10 LTS runtime; `dotnet add` defaulted to 11.x. |
| Frontend versions | React 19, Vite, Tailwind 4 | React 19.3, **Vite 8**, Tailwind 4.3, **TypeScript 7** | Current stable at time of setup. Router library deferred until a second page exists. |
| Object storage emulator | SeaweedFS in M1 | **Deferred to M4** | Nothing uses object storage before core.documents; no speculative services. |
| DB access per module | one login per module | **one login `hos_app` (NOINHERIT) + `SET ROLE hos_mod_<module>`** | One connection pool, still schema-isolated; a module that forgets to switch role has no privileges (fail closed). |

## Context
Spec §3.2 proposes React/TS + ASP.NET Core + Python FastAPI + PostgreSQL. The dev machine has Node 24, Python 3.14, uv, Docker; **no .NET SDK**. The team must maintain three languages if all are adopted.

## Decision (proposed)
| Area | Choice | Why |
|---|---|---|
| Core API | **ASP.NET Core on .NET 10 (LTS)**, Minimal APIs grouped per module, EF Core + Npgsql | Strong typing, mature for long-lived enterprise systems, good modular-monolith tooling, LTS support window |
| Arch tests | NetArchTest.Rules (or ArchUnitNET) + MSBuild reference check | Enforce module boundaries in CI |
| AI service | **Python 3.13** (pinned via uv) + FastAPI + Pydantic v2; worker via a Postgres-backed job queue (no broker initially) | Python AI ecosystem; 3.13 rather than 3.14 for wheel availability |
| Frontend | **React 19 + TypeScript + Vite + Tailwind CSS v4**, TanStack Router/Query, Radix-based components in `packages/ui-kit`, **pnpm** workspaces | Spec choice; accessible primitives |
| Database | **PostgreSQL 17** + pgvector | RLS for tenancy, schema-per-module, vectors in one store initially |
| Object storage | S3 API; dev emulator **SeaweedFS** (MinIO's community distribution changed in 2025 — avoid depending on it) | S3-compatible abstraction keeps cloud choice open |
| Identity (dev + default prod) | **Keycloak** (OIDC, MFA, Organizations, enterprise federation) | Standards-based, self-hostable for on-prem and residency; customers can federate their IdP |
| SPA auth | BFF pattern: core-api holds tokens server-side, browser gets HttpOnly cookie | Keeps tokens out of browser JS |
| Messaging | Transactional outbox/inbox in Postgres + hosted background workers | Spec: no broker until needed |
| Observability | OpenTelemetry SDKs → collector; dev: Aspire dashboard or Jaeger | Vendor-neutral |
| Local orchestration | Docker Compose (`deploy/local/compose.yaml`) + PowerShell scripts | Simple on Windows; no Kubernetes |
| CI | GitHub Actions (assumed — confirm) | Matrix builds per bundle |
| Testing | xUnit v3 + Testcontainers (.NET), pytest, Vitest, Playwright, Schemathesis (API fuzzing), k6 (load, later) | |
| Supply chain | Central package management, lockfiles, CycloneDX SBOM, `dotnet list package --vulnerable`, `pnpm audit`, `pip-audit`, gitleaks | |

## Alternatives considered
- **All-TypeScript backend (NestJS/Fastify)**: fewer languages, but weaker for long-lived regulated domain models; spec prefers .NET.
- **Python for everything**: simpler AI integration, weaker compile-time boundary enforcement.
- **Microservices from day one**: rejected — premature operational cost; modular monolith with enforced boundaries keeps extraction possible.

## Consequences
- Three language toolchains (C#, Python, TS) in CI and in the team.
- .NET 10 SDK must be installed (`winget install Microsoft.DotNet.SDK.10`).
- Docker Desktop must be running for local stack and integration tests.
