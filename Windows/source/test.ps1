param([string]$DotNet = 'dotnet', [string]$Pack, [switch]$Live, [string]$AppExe, [switch]$LiveApp)
$ErrorActionPreference = 'Stop'
$testArguments = @()
if ($Pack) { $testArguments += @('--real-pack', (Resolve-Path -LiteralPath $Pack).Path) }
if ($Live) { $testArguments += '--live' }
if ($AppExe) { $testArguments += @('--app-exe', (Resolve-Path -LiteralPath $AppExe).Path) }
if ($LiveApp) { $testArguments += '--live-app' }
& $DotNet run --project (Join-Path $PSScriptRoot 'Hats.Tests\Hats.Tests.csproj') -- @testArguments
if ($LASTEXITCODE -ne 0) { throw 'One or more checks failed.' }
