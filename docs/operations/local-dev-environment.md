# Local Development Environment (Windows + VS Code)

Last verified on the owner's machine: 2026-11-18 (see `docs/milestones/m1-progress.md`).

## Prerequisites

| Tool | Version | Owner machine (2026-11-18) | Install |
|---|---|---|---|
| Git | 2.4x+ | ✅ 2.53 | — |
| .NET SDK | 10.0.4xx (pinned in `global.json`) | ✅ 10.0.404 | `winget install Microsoft.DotNet.SDK.10` |
| Node.js | 24 LTS | ✅ 24.15 | — |
| pnpm | 10.x (pinned in `package.json`) | ✅ 10.34.9 | `npm install -g pnpm@10` |
| Python | 3.14, **python.org signed build** | ✅ 3.14.4 at `C:\Python314` | python.org installer |
| uv | latest | ✅ 0.11 | — |
| **WSL2** | — | ❌ **not installed** | Admin PowerShell: `wsl --install`, then reboot |
| Docker Desktop | current, WSL2 backend | ⚠ installed, engine can't start without WSL2 | Start after WSL2 is installed |

Hardware: 16 GB RAM minimum, 32 GB recommended.

## ⚠ Windows Smart App Control
This machine has **Smart App Control = On**. It blocks unsigned executables and DLLs that have no reputation, which includes some binaries we compile ourselves (observed: `HealthOS.CoreApi.Tests.dll` blocked, other test DLLs allowed) and uv-generated launchers. Symptoms: `An Application Control policy has blocked this file (0x800711C7 / os error 4551)`.

Options (owner's decision, since it's a security trade-off on the PC):
1. **Turn Smart App Control off** (Windows Security → App & browser control → Smart App Control). This is what Microsoft recommends for development machines. On some Windows builds it can't be turned back on without resetting Windows, so check before you switch it off.
2. Keep it on and run .NET tests only in CI (GitHub Actions) or inside a container once WSL2/Docker work.
3. Develop in a cloud dev environment (GitHub Codespaces / Dev Box).

Workarounds already applied: the Python venv uses the PSF-signed interpreter, and Python tools run as `python -m <tool>` because uv's `.exe` shims are unsigned. `mypy` can't run locally (its compiled DLLs are blocked), so it runs in CI.

## Commands

```powershell
# one-time
pnpm install
uv sync --directory services/ai-service --python C:\Python314\python.exe

# all checks that don't need containers
./scripts/test.ps1

# local stack (needs WSL2 + Docker)
./scripts/dev-up.ps1        # generates .env secrets, starts postgres(:5433)/keycloak/jaeger/mailpit, sets user-secrets, runs db bootstrap + migrate
dotnet run --project services/core-api/src/HealthOS.CoreApi     # http://localhost:5080
pnpm --filter @healthos/admin-web dev                             # http://localhost:5173
uv run --directory services/ai-service --no-sync python -m uvicorn healthos_ai.main:app --port 5090
./scripts/dev-down.ps1      # -Purge also deletes local data
```

Local URLs: admin-web http://localhost:5173 · core-api http://localhost:5080 · Keycloak http://localhost:8080 · Jaeger http://localhost:16686 · Mailpit http://localhost:8025.

## CI
`.github/workflows/ci.yml` — jobs: `dotnet` (locked restore, build with warnings-as-errors and NuGet audit, tests), `ai-service` (ruff, mypy, pytest, pip-audit), `web` (typecheck, vitest + axe, build, pnpm audit), `security` (gitleaks, CycloneDX SBOM). **Not yet run: the repository has no GitHub remote.**
