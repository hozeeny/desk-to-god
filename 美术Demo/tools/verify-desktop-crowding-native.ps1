param([switch]$RunNative, [string]$SourceProfile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
if (-not $SourceProfile) { $SourceProfile = Join-Path $projectRoot '美术Demo/verification/demo-completeness-20261023/normal-player/profiles/verify-completeness-normal-20261023-r1' }
$SourceProfile = [IO.Path]::GetFullPath($SourceProfile)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$buildRoot = Join-Path $projectRoot ('美术Demo/verification/desktop-crowding-native/' + $stamp)
$snapshot = Join-Path $buildRoot 'sources'
$evidence = Join-Path $projectRoot ('美术Demo/qa/desktop-accept-crowding-native-' + $stamp)
$utf8 = New-Object Text.UTF8Encoding($true)
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
$production = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name)
$production | Copy-Item -Destination $snapshot
$productionHashes = @($production | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Source=$_.FullName; SHA256=(Get-FileHash -LiteralPath (Join-Path $snapshot $_.Name) -Algorithm SHA256).Hash } })
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopCrowdingNative.cs') -Destination $snapshot
[IO.File]::WriteAllText((Join-Path $buildRoot 'production-sources.json'), (ConvertTo-Json -InputObject $productionHashes -Depth 4), $utf8)
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$snapshotHashes = @($sources | ForEach-Object { [pscustomobject]@{ Name=[IO.Path]::GetFileName($_); SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'compiled-sources.json'), (ConvertTo-Json -InputObject $snapshotHashes -Depth 3), $utf8)
$toolHashes = @('DesktopCrowdingNative.cs','verify-desktop-crowding-native.ps1' | ForEach-Object { [pscustomobject]@{ Name=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'tool-sources.json'), (ConvertTo-Json -InputObject $toolHashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $buildRoot 'DesktopCrowdingNative.exe'
# A winexe has no console window to activate. Only the normal production
# NOACTIVATE layered actors are shown; the runner's owner Form stays hidden.
$compilerArguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/main:NetBunny.DesktopCrowdingNative',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll','/reference:Microsoft.CSharp.dll','/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),('/win32icon:' + (Join-Path $sourceRoot 'knight.ico')),('/out:' + $executable)) + $sources
& $compiler @compilerArguments 2>&1 | Tee-Object -FilePath (Join-Path $buildRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Crowding native fixture compilation failed; no GUI was started.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($executable + '.config')
$drift = @($productionHashes | Where-Object { (Get-FileHash -LiteralPath $_.Source -Algorithm SHA256).Hash -ne $_.SHA256 })
[IO.File]::WriteAllText((Join-Path $buildRoot 'production-drift.json'), (ConvertTo-Json -InputObject @($drift) -Depth 4), $utf8)
if ($drift.Count) { throw ('Production changed while snapshotting; retain build and rebuild from the frozen sources: ' + $buildRoot) }
Write-Host ('Crowding native build: ' + $buildRoot)
if (-not $RunNative) { Write-Host 'Build only: no process, Form, GUI, profile clone, scan or input was started.'; return }

function Read-Hashes([string]$Root, [string[]]$Names) {
    $values = @{}
    foreach ($name in $Names) {
        $path = Join-Path $Root $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Required archive is missing: ' + $path) }
        $values[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    return $values
}
function Same-Hashes($First, $Second) {
    if ($First.Count -ne $Second.Count) { return $false }
    foreach ($name in $First.Keys) { if (-not $Second.ContainsKey($name) -or $First[$name] -ne $Second[$name]) { return $false } }
    return $true
}
function Read-FormalHashes {
    $root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnightAssistant'
    $hashes = @{}
    if (Test-Path -LiteralPath $root) {
        foreach ($file in Get-ChildItem -LiteralPath $root -File) {
            if ($file.Name -match '^(adventure-v1|cultivation-v2|baiye-v1|lingyu-v1|dongtian-v1|maintenance-consent|maintenance-results-v1)\.') {
                $hashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    }
    return $hashes
}
$mainNames = @('adventure-v1.txt','cultivation-v2.txt','baiye-v1.txt','lingyu-v1.txt','dongtian-v1.txt')
$attestations = @(
    [pscustomobject]@{ Path=(Join-Path $projectRoot '美术Demo/qa/desktop-accept-completeness-normal-x1-20261023-r1/desktop-acceptance.json'); SHA256='5E62534B2D42EE0B7A42CCF71737CC3D0F3651476BE2966F831F0AAA5B0D81C7' },
    [pscustomobject]@{ Path=(Join-Path $projectRoot '美术Demo/qa/desktop-accept-completeness-upgrade-restart-20261023-r1/desktop-acceptance.json'); SHA256='CE94D75A3E4B5247C29F8DDA1BE9ED5F299E8DA8713481B694A688542D7333FE' }
)
foreach ($item in $attestations) {
    if ((Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash -ne $item.SHA256) { throw 'Natural x1 source-profile report attestation changed.' }
    $attested = Get-Content -LiteralPath $item.Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($attested.profile) -ine $SourceProfile) { throw 'The chosen source profile does not match both attested natural x1 reports.' }
}
$profile = Join-Path $buildRoot 'profiles/verify-crowding-native'
if ((Test-Path -LiteralPath $profile) -or (Test-Path -LiteralPath $evidence)) { throw 'Private clone and raw evidence must be new.' }
$sourceBefore = Read-Hashes $SourceProfile $mainNames
[IO.Directory]::CreateDirectory($profile) | Out-Null
foreach ($name in $mainNames) { Copy-Item -LiteralPath (Join-Path $SourceProfile $name) -Destination (Join-Path $profile $name) }
$copied = Read-Hashes $profile $mainNames
if (-not (Same-Hashes $sourceBefore $copied)) { throw 'The five-profile clone is not byte-identical.' }
$clone = [pscustomobject]@{ SourceProfile=$SourceProfile; PrivateProfile=$profile; SourceBefore=$sourceBefore; Copied=$copied; Attestations=$attestations;
    Scope='Only these five main archives copied; no consent, restart marker, seed or backup copied. One initial synthetic geometry fixture; not natural growth or final EXE.' }
[IO.File]::WriteAllText((Join-Path $buildRoot 'profile-clone.json'), (ConvertTo-Json -InputObject $clone -Depth 6), $utf8)
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class DesktopCrowdingLaunchNative { [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }'
$foregroundBefore = [DesktopCrowdingLaunchNative]::GetForegroundWindow().ToInt64()
$realBefore = Read-FormalHashes
$arguments = @((Join-Path $sourceRoot 'Assets'),(Join-Path $sourceRoot 'ArtDemoAssets'),$evidence,$profile)
$quoted = @($arguments | ForEach-Object { '"' + $_ + '"' })
$ownProcess = $null; $ownHandle = $null; $timedOut = $false; $childExit = $null; $runError = $null
$runtime = [Diagnostics.Stopwatch]::StartNew()
try {
    $ownProcess = Start-Process -FilePath $executable -ArgumentList $quoted -WindowStyle Normal -PassThru `
        -RedirectStandardOutput (Join-Path $buildRoot 'native-stdout.log') -RedirectStandardError (Join-Path $buildRoot 'native-stderr.log')
    $ownHandle = $ownProcess.Handle
    $launch = [pscustomobject]@{ Pid=$ownProcess.Id; StartTimeUtc=$ownProcess.StartTime.ToUniversalTime().ToString('o'); Executable=$executable;
        SHA256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash; Arguments=$arguments; WindowStyle='Normal'; Target='winexe, no console activation';
        ParentDeadlineSeconds=25; ChildDeadlineSeconds=24; ObservationSeconds=12; NativeInputs=0; Scope='Source-compiled bounded synthetic layout only; only the retained handle of this exact started process can be stopped.' }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'launch.json'), (ConvertTo-Json -InputObject $launch -Depth 4), $utf8)
    while (-not $ownProcess.WaitForExit([Math]::Max(1,[Math]::Min(500,[int](25000-$runtime.ElapsedMilliseconds))))) {
        if ($runtime.Elapsed.TotalSeconds -ge 25) {
            $timedOut = $true; $ownProcess.Kill(); [void]$ownProcess.WaitForExit(3000); break
        }
    }
    $ownProcess.Refresh(); $childExit = $ownProcess.ExitCode
} catch {
    $runError = $_.Exception.ToString()
    if ($null -ne $ownProcess -and $null -ne $ownHandle) {
        try { if (-not $ownProcess.HasExited) { $ownProcess.Kill(); [void]$ownProcess.WaitForExit(3000) } }
        catch { $runError += [Environment]::NewLine + 'Own-child cleanup: ' + $_.Exception.ToString() }
    }
} finally {
    $sourceAfter = Read-Hashes $SourceProfile $mainNames; $realAfter = Read-FormalHashes
    $sourceSame = Same-Hashes $sourceBefore $sourceAfter; $realSame = Same-Hashes $realBefore $realAfter
    $foregroundAfter = [DesktopCrowdingLaunchNative]::GetForegroundWindow().ToInt64()
    $isolation = [pscustomobject]@{ SourceBefore=$sourceBefore; SourceAfter=$sourceAfter; SourceUnchanged=$sourceSame; FormalBefore=$realBefore; FormalAfter=$realAfter;
        RealArchivesUnchanged=$realSame; ForegroundBefore=$foregroundBefore; ForegroundAfter=$foregroundAfter; ForegroundUnchanged=($foregroundBefore -eq $foregroundAfter);
        TimedOut=$timedOut; ChildExitCode=$childExit; Error=$runError; ElapsedSeconds=$runtime.Elapsed.TotalSeconds; BuildRoot=$buildRoot; Evidence=$evidence }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'isolation.json'), (ConvertTo-Json -InputObject $isolation -Depth 6), $utf8)
    if ($null -ne $ownProcess) { $ownProcess.Dispose() }
}
if ($runError -or $timedOut -or -not $sourceSame -or -not $realSame -or $foregroundBefore -ne $foregroundAfter -or $null -eq $childExit -or $childExit -ne 0) { throw ('Owned crowding native failed; retain ' + $buildRoot + ' and ' + $evidence) }
$reportPath = Join-Path $evidence 'crowding-native.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw ('Native crowding report missing; retain ' + $evidence) }
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($report.passed -isnot [bool] -or -not $report.passed -or $report.nativeInputs -ne 0 -or $report.captures.Count -ne 2) {
    throw ('Native crowding business report rejected the observation; retain ' + $evidence)
}
Write-Host ('Crowding native PASS: ' + $report.elapsedSeconds + ' seconds, two raw captures, no inputs; ' + $evidence)
