$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$envFile = Join-Path $repo 'deploy/.env'
if (!(Test-Path -LiteralPath $envFile)) { throw 'Run scripts/start-infra.ps1 first.' }
$devPassword = ((Get-Content -LiteralPath $envFile | Where-Object { $_ -like 'MSSQL_SA_PASSWORD=*' }) -replace '^MSSQL_SA_PASSWORD=', '')
$env:ConnectionStrings__Database = "Server=sql;Database=EnglishHub;User Id=sa;Password=$devPassword;TrustServerCertificate=True;Encrypt=False"
# VI: Dùng cùng Linux runtime với CI; không đổi chính sách Windows. EN: Use the CI runtime without changing Windows execution policy.
docker run --rm --name englishhub-api --network englishhub_default -p 127.0.0.1:5188:5188 --env ConnectionStrings__Database -e ASPNETCORE_ENVIRONMENT=Development -e Database__Provider=SqlServer -e Database__Initialize=true -e Seed__Demo=true -v "${repo}:/repo" -v englishhub-nuget:/root/.nuget/packages -v englishhub-keys:/root/.aspnet/DataProtection-Keys -w /repo mcr.microsoft.com/dotnet/sdk:10.0 dotnet run --project src/EnglishHub.Api --urls http://0.0.0.0:5188
if ($LASTEXITCODE -ne 0) { throw 'API host failed.' }
