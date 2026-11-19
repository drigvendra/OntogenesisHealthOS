<#
.SYNOPSIS
  Starts the HealthOS local stack (PostgreSQL, Keycloak, Jaeger, Mailpit) with synthetic data only.
  On first run, generates random local secrets into .env (git-ignored) and stores the core-api
  connection settings in .NET user-secrets.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker engine is not running. Start Docker Desktop (requires WSL2) and retry.' }

function New-Secret([int]$bytes = 32) {
    $b = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    return ([Convert]::ToBase64String($b)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) {
    $lines = @(
        "HOS_PG_SUPERUSER_PASSWORD=$(New-Secret)",
        "HOS_MIGRATOR_PASSWORD=$(New-Secret)",
        "HOS_APP_PASSWORD=$(New-Secret)",
        "HOS_KEYCLOAK_ADMIN_PASSWORD=$(New-Secret)",
        "HOS_BFF_CLIENT_SECRET=$(New-Secret)",
        "HOS_DEV_USER_PASSWORD=Dev-$(New-Secret 9)",
        "HEALTHOS_AI_CONTEXT_SIGNING_KEY=$(New-Secret 48)",
        "HOS_DEV_TOTP_SECRET=$(New-Secret 10)"
    )
    [IO.File]::WriteAllText($envFile, ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))
    Write-Host 'Generated .env with random local secrets.'
}

$cfg = @{}
Get-Content $envFile | Where-Object { $_ -match '^[A-Z_]+=' } | ForEach-Object {
    $k, $v = $_ -split '=', 2; $cfg[$k] = $v
}

docker compose --env-file $envFile -f deploy/local/compose.yaml up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed.' }

$api = 'services/core-api/src/HealthOS.CoreApi'
$pg = 'Host=localhost;Port=5433'
dotnet user-secrets set 'Database:ConnectionString' "$pg;Database=healthos;Username=hos_app;Password=$($cfg['HOS_APP_PASSWORD'])" --project $api | Out-Null
dotnet user-secrets set 'Authentication:Oidc:ClientSecret' $cfg['HOS_BFF_CLIENT_SECRET'] --project $api | Out-Null

# Database roles, module schemas and migrations (idempotent).
dotnet build $api -v q | Out-Null
$dll = Join-Path $root "$api/bin/Debug/net10.0/HealthOS.CoreApi.dll"
$env:Database__AdminConnectionString = "$pg;Database=postgres;Username=hos_superuser;Password=$($cfg['HOS_PG_SUPERUSER_PASSWORD'])"
$env:Database__MigratorPassword = $cfg['HOS_MIGRATOR_PASSWORD']
$env:Database__AppPassword = $cfg['HOS_APP_PASSWORD']
$env:Database__MigratorConnectionString = "$pg;Database=healthos;Username=hos_migrator;Password=$($cfg['HOS_MIGRATOR_PASSWORD'])"
try {
    dotnet $dll db bootstrap; if ($LASTEXITCODE -ne 0) { throw 'db bootstrap failed' }
    dotnet $dll db migrate;   if ($LASTEXITCODE -ne 0) { throw 'db migrate failed' }
} finally {
    Remove-Item Env:Database__AdminConnectionString, Env:Database__MigratorPassword, Env:Database__AppPassword, Env:Database__MigratorConnectionString -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Local stack is up:'
Write-Host '  Keycloak   http://localhost:8080   (realm healthos-dev; users synthetic.admin / synthetic.user, password in .env HOS_DEV_USER_PASSWORD)'
Write-Host '  Jaeger     http://localhost:16686'
Write-Host '  Mailpit    http://localhost:8025'
Write-Host 'Next: dotnet run --project services/core-api/src/HealthOS.CoreApi   and   pnpm --filter @healthos/admin-web dev'
