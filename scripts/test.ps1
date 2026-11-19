<#
.SYNOPSIS
  Runs all automated checks that do not need the container stack: .NET (unit, host, architecture),
  ai-service (pytest, ruff), web (typecheck, vitest, audit). Exits non-zero if anything fails.
#>
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$failed = @()

function Step([string]$name, [scriptblock]$cmd) {
    Write-Host "`n=== $name" -ForegroundColor Cyan
    & $cmd
    if ($LASTEXITCODE -ne 0) { $script:failed += $name }
}

Step '.NET build + tests' { dotnet test --solution HealthOS.slnx }
Step 'ai-service pytest' { uv run --directory services/ai-service --no-sync python -m pytest }
Step 'ai-service ruff' { uv run --directory services/ai-service --no-sync python -m ruff check . }
Step 'web typecheck' { pnpm -r typecheck }
Step 'web tests' { pnpm -r test }
Step 'web audit' { pnpm audit --audit-level moderate }

if ($failed.Count -gt 0) {
    Write-Host "`nFAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "`nAll checks passed." -ForegroundColor Green
