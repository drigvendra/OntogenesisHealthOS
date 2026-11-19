<#
.SYNOPSIS
  Stops the local stack. Add -Purge to also delete local volumes (all local synthetic data).
#>
param([switch]$Purge)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dockerArgs = @('compose', '--env-file', (Join-Path $root '.env'), '-f', (Join-Path $root 'deploy/local/compose.yaml'), 'down')
if ($Purge) { $dockerArgs += '--volumes' }
docker @dockerArgs
