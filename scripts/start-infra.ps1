param([switch]$Social, [switch]$Calls,
    [ValidateRange(512,1536)][int]$SqlBufferPoolMb = $(if($env:EH_SQL_BUFFER_POOL_MB){[int]$env:EH_SQL_BUFFER_POOL_MB}else{1024}))
$ErrorActionPreference = 'Stop'
if($SqlBufferPoolMb -lt 512 -or $SqlBufferPoolMb -gt 1536){throw 'SqlBufferPoolMb/EH_SQL_BUFFER_POOL_MB must be between 512 and 1536 for this local demo.'}
$repo = Split-Path $PSScriptRoot
$envFile = Join-Path $repo 'deploy/.env'
# VI: Không commit mật khẩu, chỉ tạo một lần. EN: Generate once and never commit local credentials.
if (!(Test-Path -LiteralPath $envFile)) {
    $random = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(20))
    "MSSQL_SA_PASSWORD=Eh_${random}!9a" | Set-Content -LiteralPath $envFile -Encoding utf8
}
$composeArgs = @('compose', '--env-file', $envFile, '-f', (Join-Path $repo 'deploy/compose.yaml'))
if (!(Get-Content -LiteralPath $envFile | Where-Object { $_ -like 'AZURITE_ACCOUNT_KEY=*' })) {
    $storageKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
    "AZURITE_ACCOUNT_KEY=$storageKey" | Add-Content -LiteralPath $envFile -Encoding utf8
}
if ($Social) { $composeArgs += @('--profile', 'social') }
if ($Calls) { $composeArgs += @('--profile', 'calls') }
& docker @composeArgs up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'Docker infrastructure did not become ready.' }
# VI: Giới hạn process 2 GiB không thay thế giới hạn buffer pool. Chừa chỗ cho SQLPAL/worker và chỉ cấu hình SQL của compose này.
# EN: A 2 GiB process limit does not replace the buffer-pool limit. Reserve SQLPAL/worker headroom and target only this compose project's SQL service.
$priorSqlBufferPassword=$env:SQLCMDPASSWORD
try {
    $env:SQLCMDPASSWORD=((Get-Content -LiteralPath $envFile|Where-Object {$_ -like 'MSSQL_SA_PASSWORD=*'}) -replace '^MSSQL_SA_PASSWORD=','')
    $sqlBufferQuery=@"
SET NOCOUNT ON;
DECLARE @advanced int=(SELECT CONVERT(int,value_in_use) FROM sys.configurations WHERE name='show advanced options');
IF @advanced=0 BEGIN EXEC sp_configure 'show advanced options',1; RECONFIGURE; END;
BEGIN TRY
    EXEC sp_configure 'max server memory (MB)', $SqlBufferPoolMb; RECONFIGURE;
END TRY
BEGIN CATCH
    IF @advanced=0 BEGIN EXEC sp_configure 'show advanced options',0; RECONFIGURE; END;
    THROW;
END CATCH;
IF @advanced=0 BEGIN EXEC sp_configure 'show advanced options',0; RECONFIGURE; END;
IF NOT EXISTS (SELECT 1 FROM sys.configurations WHERE name='max server memory (MB)' AND CONVERT(int,value_in_use)=$SqlBufferPoolMb) THROW 51000,'SQL buffer-pool configuration was not applied.',1;
"@
    & docker @composeArgs exec -T --env SQLCMDPASSWORD sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -l 10 -t 20 -d master -Q $sqlBufferQuery -o /dev/null
    if($LASTEXITCODE -ne 0){throw 'SQL buffer-pool configuration failed; application startup has not been reported ready.'}
} finally {
    if($null -eq $priorSqlBufferPassword){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$priorSqlBufferPassword}
}
Write-Host "EnglishHub SQL buffer pool: $SqlBufferPoolMb MiB (local demo)."
