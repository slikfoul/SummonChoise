param([string]$Profile = 'Default', [string]$ValheimDir = 'G:\Games\SteamLibrary\steamapps\common\Valheim', [string]$DataRoot = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'BuildPackage.ps1')
& dotnet run --project (Join-Path $PSScriptRoot 'tests\SelectionChecks\SelectionChecks.csproj') -c Release -- (Join-Path $ValheimDir 'valheim_Data\Managed\assembly_valheim.dll') (Join-Path $PSScriptRoot 'bin\Release\SkeletonChoice.dll')
if ($LASTEXITCODE -ne 0) { throw 'Selection checks failed.' }
& (Join-Path $PSScriptRoot 'RegisterLocalMod.ps1') -Profile $Profile -DataRoot $DataRoot
