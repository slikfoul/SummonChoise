$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureRoot = Join-Path $projectRoot ('artifacts\install-fixture-' + [Guid]::NewGuid().ToString('N'))
$savedAppData = $env:APPDATA
# This isolated process simulates closed applications and a disposable profile.
function Get-Process { @() }
try {
    $env:APPDATA = $fixtureRoot
    $profileRoot = Join-Path $fixtureRoot 'r2modmanPlus-local\Valheim\profiles\Test'
    $oldDirectory = Join-Path $profileRoot 'BepInEx\plugins\ITBunnies-Skeleton_Selector\SkeletonSelector'
    $configDirectory = Join-Path $profileRoot 'BepInEx\config'
    New-Item -ItemType Directory -Path $oldDirectory,$configDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\SkeletonChoice.dll') -Destination (Join-Path $oldDirectory 'SkeletonSelector.dll')
    $originalOldHash = (Get-FileHash -LiteralPath (Join-Path $oldDirectory 'SkeletonSelector.dll')).Hash
    $registry = Join-Path $profileRoot 'mods.yml'
    $unrelated = "- manifestVersion: 1`n  name: Test-Unrelated`n  enabled: true`n  description: preserve exactly`n"
    [IO.File]::WriteAllText($registry, $unrelated + "- manifestVersion: 1`n  name: ITBunnies-Skeleton_Selector`n  enabled: true`n")
    $config = Join-Path $configDirectory 'Slikfoul.SkeletonChoice.cfg'
    [IO.File]::WriteAllText($config, '[1 - General]' + "`nSkeleton type = ShieldWarrior`n")
    $configHash = (Get-FileHash -LiteralPath $config).Hash
    & (Join-Path $projectRoot 'RegisterLocalMod.ps1') -Profile Test
    $first = [IO.File]::ReadAllText($registry)
    if (-not $first.StartsWith($unrelated)) { throw 'Unrelated profile entry changed.' }
    if ($first -notmatch '(?s)name: ITBunnies-Skeleton_Selector\r?\n  enabled: false') { throw 'Old selector was not disabled.' }
    if ((Get-FileHash -LiteralPath (Join-Path $oldDirectory 'SkeletonSelector.dll.old')).Hash -cne $originalOldHash) { throw 'Old selector payload changed.' }
    if (Test-Path -LiteralPath (Join-Path $oldDirectory 'SkeletonSelector.dll')) { throw 'Old selector remains loadable.' }
    & (Join-Path $projectRoot 'RegisterLocalMod.ps1') -Profile Test
    if ((Get-FileHash -LiteralPath $config).Hash -cne $configHash) { throw 'Existing setting was overwritten.' }
    $second = [IO.File]::ReadAllText($registry)
    if ([regex]::Matches($second, '(?m)^  name: Slikfoul-SkeletonChoice\r?$').Count -ne 1) { throw 'Repeat installation duplicated registration.' }
    $dlls = @(Get-ChildItem -LiteralPath (Join-Path $profileRoot 'BepInEx\plugins') -Recurse -File -Filter SkeletonChoice.dll)
    if ($dlls.Count -ne 1) { throw 'Repeat installation duplicated plugin.' }
    # Disabled state is also retained when reinstalling the new mod.
    $disabled = [regex]::Replace($second, '(?m)^  enabled: true(?=\r?$)', '  enabled: false')
    [IO.File]::WriteAllText($registry, $disabled)
    & (Join-Path $projectRoot 'RegisterLocalMod.ps1') -Profile Test
    if (@(Get-ChildItem -LiteralPath (Join-Path $profileRoot 'BepInEx\plugins') -Recurse -File -Filter SkeletonChoice.dll).Count) { throw 'Disabled plugin became loadable.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $profileRoot 'BepInEx\plugins') -Recurse -File -Filter SkeletonChoice.dll.old).Count -ne 1) { throw 'Disabled plugin missing.' }
    Write-Output 'Passed isolated installation checks: registration, repeat install, old selector backup/disable, unrelated registry preservation, config preservation and disabled state.'
}
finally {
    $env:APPDATA = $savedAppData
    # Fixture reports must not be mistaken for a live Default-profile installation.
    $packageVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'packaging\manifest.json') -Raw | ConvertFrom-Json).version_number
    $report = Join-Path $projectRoot ("artifacts\installation-$packageVersion.json")
    if (Test-Path -LiteralPath $report) {
        $metadata = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($metadata.profile -eq 'Test') { Move-Item -LiteralPath $report -Destination (Join-Path $fixtureRoot 'installation-validation.json') }
    }
}
