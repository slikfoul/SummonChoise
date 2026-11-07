param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'SkeletonChoice.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
$packaging = Join-Path $PSScriptRoot 'packaging'
$manifest = Get-Content -LiteralPath (Join-Path $packaging 'manifest.json') -Raw | ConvertFrom-Json
foreach ($field in @('name','version_number','website_url','description','dependencies')) {
    if ($manifest.PSObject.Properties.Name -notcontains $field) { throw "Missing manifest field: $field" }
}
$version = $manifest.version_number
if ($manifest.name -cne 'SkeletonChoice' -or $version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Invalid package name/version.' }
if ($manifest.description.Length -eq 0 -or $manifest.description.Length -gt 250) { throw 'Invalid description length.' }
if (@($manifest.dependencies).Count -ne 1 -or $manifest.dependencies[0] -cne 'denikson-BepInExPack_Valheim-5.4.2351') { throw 'Unexpected dependencies.' }
$dll = Join-Path $PSScriptRoot 'bin\Release\SkeletonChoice.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -cne $version) { throw 'DLL and manifest versions differ.' }
$plugin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Plugin.cs') -Raw
if ($plugin -notmatch ('public const string PluginVersion = "' + [regex]::Escape($version) + '";')) { throw 'Plugin and manifest versions differ.' }
$project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SkeletonChoice.csproj') -Raw)
if ($project.Project.PropertyGroup.Version -cne $version) { throw 'Project and manifest versions differ.' }
$changelog = Get-Content -LiteralPath (Join-Path $packaging 'CHANGELOG.md') -Raw
$headings = @([regex]::Matches($changelog, '(?m)^## (\d+\.\d+\.\d+)\r?$'))
if (!$headings.Count -or $headings[0].Groups[1].Value -cne $version) { throw 'Changelog and manifest versions differ.' }
if ([version]$version -ge [version]'1.0.0' -and $changelog -match '(?m)^## 0\.') { throw 'Development entries must not be in the public changelog.' }
$previousVersion = [version]$version
foreach ($heading in $headings | Select-Object -Skip 1) {
    $older = [version]$heading.Groups[1].Value
    if ($older -ge $previousVersion) { throw 'Changelog versions must be unique and descending.' }
    $previousVersion = $older
}
$icon = Join-Path $packaging 'icon.png'
$png = [IO.File]::ReadAllBytes($icon)
if ($png.Length -lt 24 -or [BitConverter]::ToString($png[0..7]) -cne '89-50-4E-47-0D-0A-1A-0A') { throw 'Icon must be PNG.' }
$width = [uint32]$png[16]*16777216 + [uint32]$png[17]*65536 + [uint32]$png[18]*256 + $png[19]
$height = [uint32]$png[20]*16777216 + [uint32]$png[21]*65536 + [uint32]$png[22]*256 + $png[23]
if ($width -ne 256 -or $height -ne 256) { throw 'Icon must be 256x256.' }
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$archive = Join-Path $artifacts "Slikfoul-SkeletonChoice-$version.zip"
$temporary = Join-Path $artifacts ('package-' + [guid]::NewGuid().ToString('N') + '.zip')
$sources = [ordered]@{
    'manifest.json' = Join-Path $packaging 'manifest.json'
    'README.md' = Join-Path $packaging 'README.md'
    'CHANGELOG.md' = Join-Path $packaging 'CHANGELOG.md'
    'icon.png' = $icon
    'LICENSE' = Join-Path $PSScriptRoot 'LICENSE'
    'BepInEx/plugins/SkeletonChoice/SkeletonChoice.dll' = $dll
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $sources.Keys) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$sources[$relative],$relative) | Out-Null }
    } finally { $zip.Dispose() }
    $zip = [IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        if ($zip.Entries.Count -ne $sources.Count) { throw 'Unexpected archive contents.' }
        foreach ($relative in $sources.Keys) {
            $entry = $zip.GetEntry($relative)
            if ($null -eq $entry) { throw "Missing archive entry: $relative" }
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($hash -cne (Get-FileHash -LiteralPath $sources[$relative]).Hash) { throw "Archive content differs: $relative" }
        }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $archive -Force
    Copy-Item -LiteralPath $dll -Destination (Join-Path $artifacts "SkeletonChoice-$version.dll") -Force
    $report = [ordered]@{ version=$version; archive=$archive; archiveSha256=(Get-FileHash -LiteralPath $archive).Hash; dllSha256=(Get-FileHash -LiteralPath $dll).Hash; entries=@($sources.Keys) }
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $artifacts "package-validation-$version.json") -Encoding utf8
    Write-Output "Verified SkeletonChoice ${version}: DLL/plugin/project/manifest/changelog/archive versions, icon, dependencies, license, exact entries and SHA256."
    Get-Item -LiteralPath $archive | Select-Object FullName,Length
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
