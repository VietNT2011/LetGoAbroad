param([switch]$Social, [switch]$Calls)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$envFile = Join-Path $repo 'deploy/.env'
# VI: Không commit mật khẩu, chỉ tạo một lần. EN: Generate once and never commit local credentials.
if (!(Test-Path -LiteralPath $envFile)) {
    $random = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(20))
    "MSSQL_SA_PASSWORD=Eh_${random}!9a" | Set-Content -LiteralPath $envFile -Encoding utf8
}
$composeArgs = @('compose', '--env-file', $envFile, '-f', (Join-Path $repo 'deploy/compose.yaml'))
if ($Social) { $composeArgs += @('--profile', 'social') }
if ($Calls) { $composeArgs += @('--profile', 'calls') }
& docker @composeArgs up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'Docker infrastructure did not become ready.' }
