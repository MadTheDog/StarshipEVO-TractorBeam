param(
    [string]$Game = 'D:\SteamLibrary\steamapps\common\Starship EVO',
    [string]$Dotnet = 'dotnet',
    [switch]$WeaponAttachments
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $Dotnet run --project (Join-Path $PSScriptRoot 'tests/TractorBeam.Checks.csproj') -c Release -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'Tractor checks failed.' }
$keelVersion = if ($WeaponAttachments) { '0.1.5' } else { '0.1.2' }
$version = if ($WeaponAttachments) { '0.3.6' } else { '0.2.2' }
$keel = Join-Path $PSScriptRoot ".refs/keel/Starship-Keel-$keelVersion"
if (!(Test-Path -LiteralPath (Join-Path $keel 'Sevo.Framework.Runtime.dll'))) {
    throw "Extract the verified Starship Keel $keelVersion SDK/runtime package to $keel."
}
$dist = Join-Path $PSScriptRoot 'dist'
$package = Join-Path $dist "package-$version"
$nativeOutput = Join-Path $dist "build-$version"
& $Dotnet build (Join-Path $PSScriptRoot 'native/TractorBeam.Native.csproj') -c Release --disable-build-servers -p:UseSharedCompilation=false "-p:EnableWeaponAttachments=$($WeaponAttachments.IsPresent)" "-p:OutputPath=$nativeOutput" "-p:KeelDir=$keel" "-p:GameManaged=$Game/Starship EVO_Data/Managed" "-p:LoaderCore=$Game/BepInEx/core"
if ($LASTEXITCODE) { throw 'Tractor build failed.' }
$payload = Join-Path $package 'plugin'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $nativeOutput 'TractorBeam.dll') -Destination $payload
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'sevo.mod.json') -Raw | ConvertFrom-Json
$manifest.version = $version
if (!$WeaponAttachments) { $manifest.PSObject.Properties.Remove('repository') }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'sevo.mod.json') -Encoding utf8NoBOM
if ($WeaponAttachments) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $package
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/modular-emitter.md') -Destination (Join-Path $package 'README.md')
} else {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/legacy-emitter.md') -Destination (Join-Path $package 'README.md')
}
$asset = if ($WeaponAttachments) { "TractorBeam-$version.zip" } else { "TractorBeam-$version-preview.zip" }
$zip = Join-Path $dist $asset
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
$digest = Get-FileHash -LiteralPath $zip
if ($WeaponAttachments) {
    "$($digest.Hash.ToLowerInvariant())  $asset" | Set-Content -LiteralPath (Join-Path $dist "TractorBeam-$version.sha256.txt") -Encoding ascii
}
$digest
