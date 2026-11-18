param([string]$DotNet = 'dotnet', [string]$Output = (Join-Path $PSScriptRoot '..\application'))
$ErrorActionPreference = 'Stop'
& $DotNet publish (Join-Path $PSScriptRoot 'Hats.WinUI\Hats.WinUI.csproj') -c Release -p:Platform=x64 -o $Output
if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed.' }
