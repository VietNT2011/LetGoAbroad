param([switch]$SqlServer,[switch]$Storage)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$dockerArgs = @('run','--rm','--name','englishhub-tests')
$image='mcr.microsoft.com/dotnet/sdk:10.0'
if ($Storage) {
    $localStorageKey=((Get-Content -LiteralPath (Join-Path $repo 'deploy/.env') | Where-Object { $_ -like 'AZURITE_ACCOUNT_KEY=*' }) -replace '^AZURITE_ACCOUNT_KEY=', '')
    $env:Storage__ConnectionString="DefaultEndpointsProtocol=http;AccountName=englishhub;AccountKey=$localStorageKey;BlobEndpoint=http://azurite:10000/englishhub;"
    $env:Storage__PublicEndpoint='http://azurite:10000/englishhub'
    docker build -f (Join-Path $repo 'deploy/test.Dockerfile') -t englishhub-test-sdk $repo
    if ($LASTEXITCODE -ne 0) { throw 'Test SDK image build failed.' }
    $image='englishhub-test-sdk'; $dockerArgs += @('--env','Storage__ConnectionString','--env','Storage__PublicEndpoint')
}
if ($SqlServer -or $Storage) { $dockerArgs += @('--network','englishhub_default') }
if ($SqlServer) {
    $devPassword = ((Get-Content -LiteralPath (Join-Path $repo 'deploy/.env') | Where-Object { $_ -like 'MSSQL_SA_PASSWORD=*' }) -replace '^MSSQL_SA_PASSWORD=', '')
    $env:EH_TEST_SQL = "Server=sql;User Id=sa;Password=$devPassword;TrustServerCertificate=True;Encrypt=False"
    $dockerArgs += @('--env','EH_TEST_SQL')
}
$dockerArgs += @('-v',"${repo}:/repo",'-v','englishhub-nuget:/root/.nuget/packages','-w','/repo',$image,'dotnet','test','EnglishHub.slnx','--artifacts-path','/repo/artifacts/linux','--disable-build-servers','-m:1','--logger','console;verbosity=minimal')
if (!$Storage) { $dockerArgs += @('--filter','Category!=Storage') }
& docker @dockerArgs
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
