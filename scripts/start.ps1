$ErrorActionPreference = 'Stop'
$repo=Split-Path $PSScriptRoot
& (Join-Path $PSScriptRoot 'start-infra.ps1')
docker compose --env-file (Join-Path $repo 'deploy/.env') -f (Join-Path $repo 'deploy/compose.yaml') --profile app up -d --build
if ($LASTEXITCODE -ne 0) { throw 'Application containers failed to start.' }
$ready=$false
$startup=[Diagnostics.Stopwatch]::StartNew()
# VI: Không in URL thành công khi migration/API vẫn chưa sẵn sàng. EN: Wait for actual readiness before reporting a successful startup.
while ($startup.Elapsed.TotalSeconds -lt 60) {
    try { $response=Invoke-WebRequest 'http://127.0.0.1:5188/health/ready' -TimeoutSec 3 -UseBasicParsing; if ($response.StatusCode -eq 200) { $ready=$true; break } } catch { }
    Start-Sleep -Seconds 2
}
if (!$ready) { throw 'API is not ready. Inspect docker compose logs for the EnglishHub project.' }
Write-Host 'EnglishHub: http://127.0.0.1:5188'
