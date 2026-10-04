$ErrorActionPreference = 'Stop'
$repo=Split-Path $PSScriptRoot
& (Join-Path $PSScriptRoot 'start-infra.ps1')
docker compose --env-file (Join-Path $repo 'deploy/.env') -f (Join-Path $repo 'deploy/compose.yaml') --profile app up -d --build
if ($LASTEXITCODE -ne 0) { throw 'Application containers failed to start.' }
Write-Host 'EnglishHub: http://127.0.0.1:5188'
