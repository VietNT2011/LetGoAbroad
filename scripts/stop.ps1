$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
# VI: Chỉ dừng stack EnglishHub; giữ nguyên SQL/blob volumes. EN: Stop only EnglishHub services and preserve all persistent data volumes.
docker compose --env-file (Join-Path $repo 'deploy/.env') -f (Join-Path $repo 'deploy/compose.yaml') --profile app stop
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the EnglishHub stack.' }
