$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet publish "$root/src/PosReports.Functions/PosReports.Functions.csproj" -c Release -o "$root/artifacts/lambda"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
