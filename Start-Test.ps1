param([switch]$Smoke, [switch]$VerifyOnly, [switch]$LogicSmoke, [switch]$VisualSmoke, [switch]$Modular, [switch]$MeshSmoke)
$ErrorActionPreference = 'Stop'
$game = Join-Path $PSScriptRoot 'test-install/SEVO'
$profile = Join-Path $PSScriptRoot 'test-profile'
$evidence = Join-Path $PSScriptRoot 'local-evidence'
$identity = Get-Content -Raw -LiteralPath (Join-Path $evidence 'test-identity.json') | ConvertFrom-Json
$company = 'SEVO Tractor SandboxXX'
$product = 'SEVO TB Test'
$link = Get-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('UserProfile')) "AppData/LocalLow/$company") -Force
if ($identity.company -ne $company -or $identity.product -ne $product -or
    $link.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]$link.Target) -ne $profile) {
    throw 'Tractor Beam save isolation was not verified.'
}
$data = Join-Path $game 'Starship EVO_Data'
if ((Get-FileHash -LiteralPath (Join-Path $data 'globalgamemanagers')).Hash -ne $identity.test_settings_sha256 -or
    (Get-Content -Raw -LiteralPath (Join-Path $data 'app.info')).Trim() -ne "$company`n$product") {
    throw 'Tractor Beam sandbox game identity changed.'
}
$expectedPlugins = @('Sevo.Framework.Runtime.dll', 'Sevo.Framework.Sdk.dll', 'TractorBeam.dll')
if ($MeshSmoke) { $Modular = $true; $LogicSmoke = $true }
$definitionCount = if ($Modular) { 7 } else { 1 }
$keelVersion = if ($Modular) { '0.1.4-codex-categories' } else { '0.1.2' }
if ($VisualSmoke) { $LogicSmoke = $true }
if ($LogicSmoke) {
    $Smoke = $true
    $expectedPlugins += 'TractorBeam.LogicProbe.dll'
}
$plugins = @(Get-ChildItem -LiteralPath (Join-Path $game 'BepInEx/plugins') -Recurse -Filter '*.dll')
if (Compare-Object ($plugins.Name | Sort-Object) ($expectedPlugins | Sort-Object)) {
    throw 'Unexpected sandbox plugin inventory.'
}
foreach ($plugin in $plugins) {
    $reference = if ($plugin.Name -eq 'TractorBeam.dll') {
        if ($Modular) { Join-Path $PSScriptRoot 'dist/build-0.3.5/TractorBeam.dll' }
        else { Join-Path $PSScriptRoot 'native/bin/Release/netstandard2.1/TractorBeam.dll' }
    } elseif ($plugin.Name -eq 'TractorBeam.LogicProbe.dll') {
        Join-Path $PSScriptRoot 'tests/logic/bin/Release/netstandard2.1/TractorBeam.LogicProbe.dll'
    } else {
        Join-Path $PSScriptRoot ".refs/keel/Starship-Keel-$keelVersion/$($plugin.Name)"
    }
    if ((Get-FileHash -LiteralPath $plugin.FullName).Hash -ne (Get-FileHash -LiteralPath $reference).Hash) {
        throw "Sandbox binary differs from its reference: $($plugin.Name)"
    }
}
Write-Output "Verified Tractor Beam sandbox at $game"
if ($VerifyOnly) { return }
if (Get-Process -Name 'Starship EVO' -ErrorAction SilentlyContinue) {
    throw 'Close the other Starship EVO process before starting the isolated test.'
}

$state = Join-Path $profile 'keel'
$marker = Join-Path $state 'sevo-runtime-profile.json'
New-Item -ItemType Directory -Path $state -Force | Out-Null
if (!(Test-Path -LiteralPath $marker)) {
    @{ schemaVersion = 1; profileId = [Guid]::NewGuid().ToString(); gameDirectory = $game } |
        ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding utf8NoBOM
}
$session = [Guid]::NewGuid().ToString('N')
$run = Join-Path $evidence $session
New-Item -ItemType Directory -Path $run | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = Join-Path $game 'Starship EVO.exe'
$start.WorkingDirectory = $game
$start.UseShellExecute = $false
$start.Environment['SEVO_PLATFORM_STATE_DIR'] = $state
$start.Environment['SEVO_PLATFORM_SESSION'] = $session
$start.Environment['SEVO_PLATFORM_EXPERIMENTAL_CONTENT'] = '1'
$start.Environment['SEVO_KEEL_EXPERIMENTAL_FUNCTIONAL'] = '1'
if ($LogicSmoke) { $start.Environment['SEVO_TRACTOR_LOGIC_PROBE'] = '1' }
if ($VisualSmoke) { $start.Environment['SEVO_TRACTOR_VISUAL_ONLY'] = '1' }
if ($MeshSmoke) { $start.Environment['SEVO_TRACTOR_MESH_ONLY'] = '1' }
foreach ($arg in @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $run 'player.log'))) {
    $start.ArgumentList.Add($arg)
}
if ($Smoke) {
    if (!$LogicSmoke) { $start.ArgumentList.Add('-batchmode') }
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
}
$process = [Diagnostics.Process]::Start($start)
if (!$Smoke) {
    Write-Output "Tractor Beam test process $($process.Id). Close the game when finished."
    $process.WaitForExit()
    return
}

try {
    $deadline = [DateTime]::UtcNow.AddSeconds($(if ($LogicSmoke) { 240 } else { 45 }))
    $statusFile = Join-Path $state 'runtime-status.json'
    $logicLog = Join-Path $game 'BepInEx/LogOutput.log'
    do {
        Start-Sleep -Milliseconds 500
        $status = if (Test-Path -LiteralPath $statusFile) {
            try {
                Get-Content -Raw -LiteralPath $statusFile -ErrorAction Stop | ConvertFrom-Json
            } catch [System.IO.IOException] {
                # Keel atomically replaces this file while the game starts.
                continue
            }
        }
        if ($status.session -eq $session -and $status.processId -eq $process.Id) {
            if ($status.failed) { throw "Keel refused startup: $($status.reason)" }
            if ($status.resourcesReady -and $status.registeredDefinitions -ne $definitionCount) {
                throw "Expected $definitionCount registered Tractor Beam definitions, got $($status.registeredDefinitions)."
            }
            if ($status.resourcesReady -and $status.registrationAdmitted -and $status.registeredDefinitions -eq $definitionCount) {
                if ($LogicSmoke) {
                    $records = @(Select-String -LiteralPath $logicLog -Pattern "TRACTOR_LOGIC_PROBE (PASS|FAIL).*session=$session" -ErrorAction SilentlyContinue)
                    if (!$records) { continue }
                    if ($records[-1].Line -match 'PROBE FAIL') { throw $records[-1].Line }
                    Write-Output $records[-1].Line
                }
                $status | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $run 'status.json')
                Copy-Item -LiteralPath (Join-Path $state 'registry.json') -Destination $run
                Write-Output "Keel registered $definitionCount Tractor Beam definitions. Evidence: $run"
                return
            }
        }
    } while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline)
    $exitDescription = if ($process.HasExited) { "Exit code $($process.ExitCode)." } else { 'Startup timed out.' }
    throw "No matching Keel registration report. $exitDescription Inspect $run."
} finally {
    if (!$process.HasExited -and !($process.CloseMainWindow() -and $process.WaitForExit(4000))) {
        $process.Kill()
        $process.WaitForExit()
    }
    $log = Join-Path $game 'BepInEx/LogOutput.log'
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination $run }
    $process.Dispose()
}
