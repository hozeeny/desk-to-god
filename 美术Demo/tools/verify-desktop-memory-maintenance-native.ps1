param([switch]$RunNative)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$buildRoot = Join-Path $projectRoot ('美术Demo/verification/desktop-memory-maintenance-native/' + $stamp)
$snapshot = Join-Path $buildRoot 'sources'
$evidence = Join-Path $projectRoot ('美术Demo/qa/desktop-accept-memory-maintenance-native-' + $stamp)
$utf8 = New-Object Text.UTF8Encoding($true)
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
$production = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name)
$production | Copy-Item -Destination $snapshot
$productionHashes = @($production | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Source=$_.FullName; SHA256=(Get-FileHash -LiteralPath (Join-Path $snapshot $_.Name) -Algorithm SHA256).Hash } })
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopMemoryMaintenanceNative.cs') -Destination $snapshot
[IO.File]::WriteAllText((Join-Path $buildRoot 'production-sources.json'), (ConvertTo-Json -InputObject $productionHashes -Depth 4), $utf8)
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$snapshotHashes = @($sources | ForEach-Object { [pscustomobject]@{ Name=[IO.Path]::GetFileName($_); SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'compiled-sources.json'), (ConvertTo-Json -InputObject $snapshotHashes -Depth 3), $utf8)
$toolHashes = @('DesktopMemoryMaintenanceNative.cs','verify-desktop-memory-maintenance-native.ps1' | ForEach-Object { [pscustomobject]@{ Name=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'tool-sources.json'), (ConvertTo-Json -InputObject $toolHashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $buildRoot 'DesktopMemoryMaintenanceNative.exe'
$compilerArguments = @('/nologo','/target:exe','/platform:x64','/optimize+','/codepage:65001','/main:NetBunny.DesktopMemoryMaintenanceNative',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll','/reference:Microsoft.CSharp.dll','/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),('/win32icon:' + (Join-Path $sourceRoot 'knight.ico')),('/out:' + $executable)) + $sources
& $compiler @compilerArguments 2>&1 | Tee-Object -FilePath (Join-Path $buildRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Memory native fixture compilation failed; no GUI/worker was started.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($executable + '.config')
Write-Host ('Memory native build: ' + $buildRoot)
if (-not $RunNative) { Write-Host 'Build only: no game, child worker, Form, profile copy or native input was started.'; return }

$archiveNames = @('adventure-v1.txt','cultivation-v2.txt','baiye-v1.txt','lingyu-v1.txt','dongtian-v1.txt')
$sourceProfile = Join-Path $projectRoot '美术Demo/verification/demo-completeness-20261023/normal-player/profiles/verify-completeness-normal-20261023-r1'
$reportSpecs = @(
    [pscustomobject]@{ Path=(Join-Path $projectRoot '美术Demo/qa/desktop-accept-completeness-normal-x1-20261023-r1/desktop-acceptance.json'); SHA256='5E62534B2D42EE0B7A42CCF71737CC3D0F3651476BE2966F831F0AAA5B0D81C7'; Restart=$false },
    [pscustomobject]@{ Path=(Join-Path $projectRoot '美术Demo/qa/desktop-accept-completeness-upgrade-restart-20261023-r1/desktop-acceptance.json'); SHA256='CE94D75A3E4B5247C29F8DDA1BE9ED5F299E8DA8713481B694A688542D7333FE'; Restart=$true }
)
foreach ($spec in $reportSpecs) {
    if ((Get-FileHash -LiteralPath $spec.Path -Algorithm SHA256).Hash -ne $spec.SHA256) { throw 'The independently reviewed natural-progression/restart report changed; no profile copied.' }
    $prior = Get-Content -LiteralPath $spec.Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($prior.passed -isnot [bool] -or -not $prior.passed -or $prior.restartVerification -ne $spec.Restart -or
        -not [string]::Equals([IO.Path]::GetFullPath($prior.profile),[IO.Path]::GetFullPath($sourceProfile),[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Natural source report no longer attests this exact private profile; no copy or native launch.'
    }
}
function Read-FiveHashes([string]$root) {
    $hashes = @{}
    foreach ($name in $archiveNames) {
        $file = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or ((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw ('An exact regular main archive is required: ' + $name) }
        $hashes[$name] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    }
    return $hashes
}
function Same-Hashes($left,$right) {
    if ($left.Count -ne $right.Count) { return $false }
    foreach ($name in $left.Keys) { if (-not $right.ContainsKey($name) -or $left[$name] -ne $right[$name]) { return $false } }
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
$before = Read-FormalHashes
$sourceBefore = Read-FiveHashes $sourceProfile
$profile = Join-Path $buildRoot 'profiles/verify-memory-maintenance-native'
if ((Test-Path -LiteralPath $profile) -or (Test-Path -LiteralPath $evidence)) { throw 'Preserve evidence: private copy profile and native output must be new.' }
[IO.Directory]::CreateDirectory($profile) | Out-Null
foreach ($name in $archiveNames) { Copy-Item -LiteralPath (Join-Path $sourceProfile $name) -Destination (Join-Path $profile $name) }
$copies = Read-FiveHashes $profile
$sourceAfterCopy = Read-FiveHashes $sourceProfile
if (-not (Same-Hashes $sourceBefore $copies) -or -not (Same-Hashes $sourceBefore $sourceAfterCopy)) { throw 'Source/copy identity changed; no native launch.' }
$clonePath = Join-Path $buildRoot 'clone-provenance.json'
$clone = [pscustomobject]@{ SourceProfile=[IO.Path]::GetFullPath($sourceProfile); Profile=[IO.Path]::GetFullPath($profile); SourceBefore=$sourceBefore; CopyHashes=$copies; SourceAfterCopy=$sourceAfterCopy;
    SourceReports=$reportSpecs; Scope='Copy only five verified real x1/ten-kill/awakened private main archives byte-for-byte; no consent, restart marker, backup, seed, paused long-test source or original user archive.' }
[IO.File]::WriteAllText($clonePath, (ConvertTo-Json -InputObject $clone -Depth 6), $utf8)
$arguments = @((Join-Path $sourceRoot 'Assets'),(Join-Path $sourceRoot 'ArtDemoAssets'),$evidence,$profile,$clonePath)
$quoted = @($arguments | ForEach-Object { '"' + $_ + '"' })
$ownProcess = $null; $ownHandle = $null; $timedOut = $false; $childExit = $null; $runError = $null
$runtime = [Diagnostics.Stopwatch]::StartNew()
try {
    # This is an explicitly interactive native game, so its first normal window
    # must receive Normal startup. Its separate owned memory worker is Hidden.
    $ownProcess = Start-Process -FilePath $executable -ArgumentList $quoted -WindowStyle Normal -PassThru `
        -RedirectStandardOutput (Join-Path $buildRoot 'native-stdout.log') -RedirectStandardError (Join-Path $buildRoot 'native-stderr.log')
    $ownHandle = $ownProcess.Handle
    $launch = [pscustomobject]@{ Pid=$ownProcess.Id; StartTimeUtc=$ownProcess.StartTime.ToUniversalTime().ToString('o'); Executable=$executable;
        SHA256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash; Arguments=$arguments; WindowStyle='Normal'; AbsoluteDeadlineSeconds=240; ChildInputDeadlineSeconds=235;
        Scope='Source runner, not final EXE/natural pressure discovery; actual cloned real-grown qishi auto combat and native manual target selection. One owned hidden memory child self-leases until runner start+234s and uses nonce STOP. Parent holds/stops only this new retained native process handle.' }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'launch.json'), (ConvertTo-Json -InputObject $launch -Depth 4), $utf8)
    while (-not $ownProcess.WaitForExit([Math]::Max(1,[Math]::Min(500,[int](240000-$runtime.ElapsedMilliseconds))))) {
        if ($runtime.Elapsed.TotalSeconds -ge 240) { $timedOut=$true; $ownProcess.Kill(); [void]$ownProcess.WaitForExit(5000); break }
    }
    if (-not $ownProcess.WaitForExit(5000)) { throw 'Owned native child did not finish output redirection.' }
    $ownProcess.Refresh(); $childExit=$ownProcess.ExitCode
} catch {
    $runError=$_.Exception.ToString()
    if ($null -ne $ownProcess -and $null -ne $ownHandle) {
        try { if (-not $ownProcess.HasExited) { $ownProcess.Kill(); [void]$ownProcess.WaitForExit(5000) } }
        catch { $runError += [Environment]::NewLine + 'Only-own-child cleanup: ' + $_.Exception.ToString() }
    }
} finally {
    $after=Read-FormalHashes; $same=Same-Hashes $before $after
    $sourceAfter=Read-FiveHashes $sourceProfile; $sourceSame=Same-Hashes $sourceBefore $sourceAfter
    $isolation=[pscustomobject]@{ Before=$before; After=$after; Unchanged=$same; SourceBefore=$sourceBefore; SourceAfter=$sourceAfter; SourceUnchanged=$sourceSame;
        TimedOut=$timedOut; ChildExitCode=$childExit; Error=$runError; ElapsedSeconds=$runtime.Elapsed.TotalSeconds; BuildRoot=$buildRoot; Evidence=$evidence;
        Scope='Only the named five source files are read/copied; source and original user archives remain untouched. Short resource/native observation, long tests remain paused.' }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'formal-and-source-isolation.json'), (ConvertTo-Json -InputObject $isolation -Depth 6), $utf8)
}
if ($runError -or $timedOut -or -not $same -or -not $sourceSame -or $null -eq $childExit -or $childExit -ne 0) { throw ('Owned memory native failed; retain ' + $buildRoot + ' and ' + $evidence) }
$reportPath=Join-Path $evidence 'memory-maintenance-native.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'Missing native memory business report; child exit 0 is not a passing result.' }
$report=Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($report.passed -isnot [bool] -or -not $report.passed -or $report.realArchivesUnchanged -isnot [bool] -or -not $report.realArchivesUnchanged -or
    $report.sourceArchivesUnchanged -isnot [bool] -or -not $report.sourceArchivesUnchanged -or -not $report.allScopesOffAtExit -or -not $report.ownedChildStoppedNormally -or
    $report.memoryCallbacks -ne 1 -or $report.acceptedMemoryCallbacks -ne 1 -or $report.rejectedMemoryCallbacks -ne 0 -or $report.actualNativeTrimCalls -ne 1 -or $report.rejectedNativeTrimCalls -ne 0 -or
    $report.ownedMemoryChild.verifiedPages -ne 24576 -or $report.ownedMemoryChild.dataSHA256Before.Length -ne 64 -or
    $report.ownedMemoryChild.dataSHA256Before -ne $report.ownedMemoryChild.dataSHA256After) {
    throw ('Native memory report rejected actual flow/worker scope; retain ' + $evidence)
}
Write-Host ('Memory native PASS: ' + $report.elapsedSeconds + ' seconds; ' + $report.nativeClicks + ' native inputs; ' + $evidence)
