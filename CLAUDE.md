# CLAUDE.md — Repository rules for Ontogenesis HealthOS

The authoritative specification is `Ontogenesis_HealthOS_Modular_Claude_Code_Master.md`. If this file and the spec disagree, the spec wins until an ADR changes it.

## Current state
- M0 approved (owner chose C#/.NET). **M1 in progress** — see `docs/milestones/m1-progress.md`.
- ADR-0001 Accepted with amendments; ADRs 0002–0005 Proposed, followed by default.

## Machine quirks (owner's Windows dev box)
- Smart App Control is ON: some freshly built DLLs/EXEs are blocked (0x800711C7). Don't "fix" by changing code; report it.
- Python: use the venv from `C:\Python314` and run tools as `uv run --no-sync python -m <tool>` (uv .exe shims are blocked).
- `dotnet` may not be on PATH in new shells: prefix `$env:PATH="C:\Program Files\dotnet;$env:PATH"`.
- PowerShell 5.1 `Set-Content -Encoding utf8` writes a BOM, which breaks TOML/JSON tools; write files with the editor tools or `[IO.File]::WriteAllText` + `UTF8Encoding($false)`.

## Hard rules (never break)
1. **Synthetic data only.** No real patient identifiers in code, fixtures, logs, prompts, traces, screenshots or commit messages.
2. **Milestone gates.** Finish a milestone with a report in `docs/milestones/` (commands run, actual results, gaps) and stop for approval. Never report a test as passed unless it was executed in this environment.
3. **No fabrication.** Don't stub an integration and describe it as working. Don't claim regulatory compliance or certification anywhere (code, UI copy, docs).
4. **AI never finalizes.** AI output is stored as a draft; only an authenticated human principal with the right permission can approve/publish/sign. No AI path executes SQL, publishes configuration, prescribes, or performs irreversible operations.
5. **No production deployment** and no real PHI without explicit written authorization from the owner.

## Module boundaries (enforced by tests from M1)
- Each module = `<Module>.Contracts` (public: DTOs, commands, events, interfaces) + `<Module>` (internal implementation).
- A module may reference only: platform SDK packages, its own projects, and the **Contracts** project of a module it declares as a dependency in its `module.json`.
- **Product modules never depend on other product modules.** Cross-product features live in *bridge modules* (e.g. `bridge-clinicos-careops`) that depend on both products' contracts and are separately licensed.
- Each module owns one PostgreSQL schema and connects with its own DB role that has no grants on other schemas. No cross-schema joins.
- Cross-module communication: in-process contract interfaces (sync, same deployment) or outbox/inbox events (async). Event payloads are thin (IDs + minimal changed fields), never bulk PHI.
- HealthOS Core contains **no** clinical, dental, vision, pharma or equipment domain logic.

## Tenancy
- Every tenant-owned row has `tenant_id`; PostgreSQL Row-Level Security is enabled on every tenant-owned table; the app sets `app.tenant_id` per transaction. App DB roles never have `BYPASSRLS`.
- Every new tenant-owned table needs a cross-tenant denial test.

## Commands
- All checks without containers: `./scripts/test.ps1`
- .NET only: `dotnet test --solution HealthOS.slnx` (Microsoft Testing Platform; xUnit v3)
- Add a NuGet package: `dotnet add <proj> package <name>` (central versions in `Directory.Packages.props`; keep Microsoft.* on 10.x)
- ai-service: `uv run --directory services/ai-service --no-sync python -m pytest`
- Web: `pnpm -r typecheck`, `pnpm -r test`, `pnpm --filter @healthos/admin-web dev`
- Local stack: `./scripts/dev-up.ps1` / `./scripts/dev-down.ps1` (needs WSL2 + Docker; Postgres on host port 5433)
- Database: `dotnet <HealthOS.CoreApi.dll> db bootstrap` (admin conn) then `db migrate` (migrator conn) — both idempotent
- DB integration tests need `HEALTHOS_TEST_PG_ADMIN` (admin connection string); otherwise they are skipped
- `dotnet ef` cannot run on the owner's machine (Smart App Control); hand-written migrations are guarded by `Model_has_no_pending_changes`

## Database rules
- Module tables live in the module's schema; the module's DbContext derives from `ModuleDbContext` and is registered with `AddModuleDatabase`.
- Runtime connects as `hos_app` (NOINHERIT, no grants) and `SET ROLE hos_mod_<schema>`; migrations run as `hos_migrator`. Never grant app roles SUPERUSER/BYPASSRLS.

## Adding a module
1. `modules/<dir>/module.json` (validated by `ManifestValidator`) + `src/<Name>.Contracts` + `src/<Name>` (embed `module.json` as resource `module.json`).
2. Reference it from the host (`services/core-api`) and add it to `InstalledModules`.
3. Every endpoint: `.AllowPublic(reason)`, `.RequireAuthenticatedUser(reason)` or `.RequirePermission(code)` — exactly one.
4. Architecture tests must stay green.

## Conventions
- ADRs in `docs/adrs/NNNN-title.md` (Status: Proposed | Accepted | Superseded).
- Don't create empty folders or placeholder buttons to imply functionality.
- Pin dependencies (`Directory.Packages.props`, `pnpm-lock.yaml`, `uv.lock`).
