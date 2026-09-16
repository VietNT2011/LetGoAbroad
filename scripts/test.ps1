param([switch]$SqlServer)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$dockerArgs = @('run','--rm','--name','englishhub-tests')
if ($SqlServer) {
    $devPassword = ((Get-Content -LiteralPath (Join-Path $repo 'deploy/.env') | Where-Object { $_ -like 'MSSQL_SA_PASSWORD=*' }) -replace '^MSSQL_SA_PASSWORD=', '')
    $env:EH_TEST_SQL = "Server=sql;User Id=sa;Password=$devPassword;TrustServerCertificate=True;Encrypt=False"
    $dockerArgs += @('--network','englishhub_default','--env','EH_TEST_SQL')
}
$dockerArgs += @('-v',"${repo}:/repo",'-v','englishhub-nuget:/root/.nuget/packages','-w','/repo','mcr.microsoft.com/dotnet/sdk:10.0','dotnet','test','EnglishHub.slnx','--artifacts-path','/repo/artifacts/linux','--disable-build-servers','-m:1','--logger','console;verbosity=minimal')
& docker @dockerArgs
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
