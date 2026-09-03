# VI: Cache chỉ nằm trong checkout. EN: Keep tooling state local to this checkout.
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '../work/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '../work/nuget'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
