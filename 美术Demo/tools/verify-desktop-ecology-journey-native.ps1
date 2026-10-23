param([switch]$RunNative,[string]$OutputDirectory='',[string]$ProfileName='')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$buildRoot = Join-Path $projectRoot ('美术Demo/verification/desktop-ecology-journey-native/' + $stamp)
$snapshot = Join-Path $buildRoot 'sources'
$evidence = if([string]::IsNullOrWhiteSpace($OutputDirectory)){Join-Path $projectRoot ('美术Demo/qa/desktop-accept-ecology-journey-native-' + $stamp)}else{[IO.Path]::GetFullPath($OutputDirectory)}
if([string]::IsNullOrWhiteSpace($ProfileName)){$ProfileName='verify-ecology-journey-'+$stamp}
if($ProfileName -notmatch '^verify-ecology-journey-[a-zA-Z0-9_-]{1,40}$'){throw 'Use a fresh verify-ecology-journey- profile name of at most 63 characters.'}
$utf8 = New-Object Text.UTF8Encoding($true)
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
$production = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name)
if($production.Count -ne 60){throw 'Expected the reviewed set of exactly sixty production source files; rebuild review is required.'}
$production | Copy-Item -Destination $snapshot
$productionHashes = @($production | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Source=$_.FullName; SHA256=(Get-FileHash -LiteralPath (Join-Path $snapshot $_.Name) -Algorithm SHA256).Hash } })
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopEcologyJourneyNative.cs') -Destination $snapshot
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $buildRoot 'wrapper-snapshot.ps1')
[IO.File]::WriteAllText((Join-Path $buildRoot 'production-sources.json'), (ConvertTo-Json -InputObject $productionHashes -Depth 4), $utf8)
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$snapshotHashes = @($sources | ForEach-Object { [pscustomobject]@{ Name=[IO.Path]::GetFileName($_); SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'compiled-sources.json'), (ConvertTo-Json -InputObject $snapshotHashes -Depth 3), $utf8)
$toolHashes = @('DesktopEcologyJourneyNative.cs','verify-desktop-ecology-journey-native.ps1' | ForEach-Object { [pscustomobject]@{ Name=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'tool-sources.json'), (ConvertTo-Json -InputObject $toolHashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $buildRoot 'DesktopEcologyJourneyNative.exe'
$compilerArguments = @('/nologo','/target:exe','/platform:x64','/optimize+','/codepage:65001','/main:NetBunny.DesktopEcologyJourneyNative',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll','/reference:Microsoft.CSharp.dll','/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),('/win32icon:' + (Join-Path $sourceRoot 'knight.ico')),('/out:' + $executable)) + $sources
& $compiler @compilerArguments 2>&1 | Tee-Object -FilePath (Join-Path $buildRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Ecology journey native fixture compilation failed; no GUI was started.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($executable + '.config')
$profile = Join-Path $buildRoot ('profiles/'+$ProfileName)
$buildRecord=[pscustomobject]@{Executable=$executable;ExecutableSHA256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash;ProductionSourceCount=$production.Count;PrivateProfile=$profile;Evidence=$evidence;SourceManifest=(Join-Path $buildRoot 'production-sources.json');ToolManifest=(Join-Path $buildRoot 'tool-sources.json');Scope='Build only unless RunNative is explicitly supplied. The native journey uses one source host and writes an original-format restart marker only after its final save; a real new-process restore is separate.'}
[IO.File]::WriteAllText((Join-Path $buildRoot 'build.json'),(ConvertTo-Json -InputObject $buildRecord -Depth 4),$utf8)
Write-Host ('Ecology journey native build: ' + $buildRoot)
if (-not $RunNative) { Write-Host 'Build only: no process, Form, GUI, profile or native input was started.'; return }

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
if ((Test-Path -LiteralPath $profile) -or (Test-Path -LiteralPath $evidence)) { throw 'Preserve evidence: native profile and output must be new.' }
$arguments = @((Join-Path $sourceRoot 'Assets'),(Join-Path $sourceRoot 'ArtDemoAssets'),$evidence,$profile)
$quoted = @($arguments | ForEach-Object { '"' + $_ + '"' })
$ownProcess = $null
$ownHandle = $null
$timedOut = $false
$childExit = $null
$runError = $null
$runtime = [Diagnostics.Stopwatch]::StartNew()
$performance=New-Object 'Collections.Generic.List[object]'
$nextSample=0.0
$previousCpu=0.0
$previousSample=0.0
if(-not ('EcologyJourneyOwnResources' -as [type])){Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class EcologyJourneyOwnResources { [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags); }'}
try {
    # Explicit interactive game child: Normal is essential for the first feature
    # window. A background supervisor may remain Hidden outside this script.
    $ownProcess = Start-Process -FilePath $executable -ArgumentList $quoted -WindowStyle Normal -PassThru `
        -RedirectStandardOutput (Join-Path $buildRoot 'native-stdout.log') -RedirectStandardError (Join-Path $buildRoot 'native-stderr.log')
    $ownHandle = $ownProcess.Handle
    $launch = [pscustomobject]@{ Pid=$ownProcess.Id; StartTimeUtc=$ownProcess.StartTime.ToUniversalTime().ToString('o'); Executable=$executable;
        SHA256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash; Arguments=$arguments; WindowStyle='Normal'; AbsoluteDeadlineSeconds=480; ChildInputDeadlineSeconds=475;
        Scope='One source-compiled unseeded mortal x1 Observe profile: owned two-file maintenance, persisted native revocation, nine further ordinary manual kills, active awakening, zero-input automatic combat/cultivation. Only the retained own-child handle can be stopped; this eight-minute bound does not resume the paused long test.' }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'launch.json'), (ConvertTo-Json -InputObject $launch -Depth 4), $utf8)
    while (-not $ownProcess.WaitForExit([Math]::Max(1, [Math]::Min(500, [int](480000 - $runtime.ElapsedMilliseconds))))) {
        if($runtime.Elapsed.TotalSeconds -ge $nextSample){
            try {
                $ownProcess.Refresh()
                $sampleSeconds=$runtime.Elapsed.TotalSeconds
                $cpuSeconds=$ownProcess.TotalProcessorTime.TotalSeconds
                $duration=$sampleSeconds-$previousSample
                $cpuDelta=$cpuSeconds-$previousCpu
                $progress=$null
                $progressPath=Join-Path $evidence 'progress.json'
                if(Test-Path -LiteralPath $progressPath){try{$progress=Get-Content -LiteralPath $progressPath -Raw -Encoding UTF8|ConvertFrom-Json}catch{}}
                $performance.Add([pscustomobject]@{ElapsedSeconds=$sampleSeconds;IntervalSeconds=$duration;CpuSecondsDelta=$cpuDelta;CpuOneCorePercent=if($duration -gt 0){100*$cpuDelta/$duration}else{0};CpuMachinePercent=if($duration -gt 0){100*$cpuDelta/$duration/[Environment]::ProcessorCount}else{0};PrivateMiB=$ownProcess.PrivateMemorySize64/1MB;WorkingSetMiB=$ownProcess.WorkingSet64/1MB;Handles=$ownProcess.HandleCount;Gdi=[EcologyJourneyOwnResources]::GetGuiResources($ownHandle,0);User=[EcologyJourneyOwnResources]::GetGuiResources($ownHandle,1);Stage=if($null -ne $progress){$progress.stage}else{'startup'};MortalKills=if($null -ne $progress){$progress.MortalKills}else{$null}})
                $previousCpu=$cpuSeconds;$previousSample=$sampleSeconds
            }catch{$performance.Add([pscustomobject]@{ElapsedSeconds=$runtime.Elapsed.TotalSeconds;SamplingError=$_.Exception.Message})}
            $nextSample=$runtime.Elapsed.TotalSeconds+2
        }
        if ($runtime.Elapsed.TotalSeconds -ge 480) {
            $timedOut = $true
            # This object and retained native handle came only from the child
            # just created above; no PID/name enumeration or other-app cleanup.
            $ownProcess.Kill(); if (-not $ownProcess.WaitForExit(5000)) { throw 'The owned timed-out child did not report exit.' }; break
        }
    }
    if (-not $ownProcess.WaitForExit(5000)) { throw 'The owned child did not finish redirecting its output.' }
    $ownProcess.Refresh(); $childExit = $ownProcess.ExitCode
} catch {
    $runError = $_.Exception.ToString()
    if ($null -ne $ownProcess -and $null -ne $ownHandle) {
        try {
            if (-not $ownProcess.HasExited) { $ownProcess.Kill(); [void]$ownProcess.WaitForExit(5000) }
        } catch { $runError += [Environment]::NewLine + 'Own-child cleanup: ' + $_.Exception.ToString() }
    }
}
finally {
    $after = Read-FormalHashes
    $same = $before.Count -eq $after.Count
    foreach ($name in $before.Keys) { if (-not $after.ContainsKey($name) -or $before[$name] -ne $after[$name]) { $same = $false } }
    $isolation = [pscustomobject]@{ Before=$before; After=$after; Unchanged=$same; TimedOut=$timedOut; ChildExitCode=$childExit; Error=$runError;
        ElapsedSeconds=$runtime.Elapsed.TotalSeconds; BuildRoot=$buildRoot; Evidence=$evidence; Scope='Short explicit native UI fixture only; long natural-time tests remain paused.' }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'formal-isolation.json'), (ConvertTo-Json -InputObject $isolation -Depth 5), $utf8)
    if($performance.Count -gt 0){$performance|Export-Csv -LiteralPath (Join-Path $buildRoot 'short-journey-performance.csv') -NoTypeInformation -Encoding UTF8}
    $validSamples=@($performance|Where-Object {$_.PSObject.Properties.Name -contains 'IntervalSeconds'})
    $totalCpu=($validSamples|Measure-Object -Property CpuSecondsDelta -Sum).Sum
    $totalInterval=($validSamples|Measure-Object -Property IntervalSeconds -Sum).Sum
    $perfSummary=[pscustomobject]@{Samples=$validSamples.Count;SamplingErrors=($performance.Count-$validSamples.Count);ObservedSeconds=$totalInterval;CpuSeconds=$totalCpu;CpuMachinePercent=if($totalInterval -gt 0){100*$totalCpu/$totalInterval/[Environment]::ProcessorCount}else{$null};Scope='Own retained child process only. Includes source fixture observation, native screenshots, input and verification costs; short explicit journey, not a long-duration or all-scene benchmark.'}
    [IO.File]::WriteAllText((Join-Path $buildRoot 'short-journey-performance.json'),(ConvertTo-Json -InputObject $perfSummary -Depth 3),$utf8)
    if($null -ne $ownProcess){$ownProcess.Dispose()}
}
function Fail-Journey([string]$message){
    $failurePath=Join-Path $buildRoot 'terminal-failure.json'
    if(-not (Test-Path -LiteralPath $failurePath)){[IO.File]::WriteAllText($failurePath,(ConvertTo-Json -InputObject ([pscustomobject]@{Message=$message;Error=$runError;TimedOut=$timedOut;ChildExitCode=$childExit;Evidence=$evidence;Progress=(Join-Path $evidence 'progress.json');Scope='Terminal failure is retained; child report and native stdout/stderr are never changed into success.'}) -Depth 4),$utf8)}
    throw $message
}
if ($runError -or $timedOut -or -not $same -or $null -eq $childExit -or $childExit -ne 0) { Fail-Journey ('Owned native ecology journey fixture failed; retained ' + $buildRoot + ' and ' + $evidence) }
$reportPath = Join-Path $evidence 'ecology-journey-native.json'
if (-not (Test-Path -LiteralPath $reportPath)) { Fail-Journey ('Native ecology journey report is missing; retained ' + $evidence) }
try{$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json}catch{Fail-Journey ('Native ecology journey report cannot be parsed; retained '+$evidence+'; '+$_.Exception.Message)}
if ($report.passed -isnot [bool] -or -not $report.passed -or
    $report.realArchivesUnchanged -isnot [bool] -or -not $report.realArchivesUnchanged -or
    $report.cleanCalls -ne 1 -or $report.acceptedCleanCalls -ne 1 -or $report.rejectedCleanCalls -ne 0 -or
    $report.journeyComplete -isnot [bool] -or -not $report.journeyComplete -or $report.journeyKillEvents.Count -ne 9 -or
    $report.automaticZeroInput -isnot [bool] -or -not $report.automaticZeroInput -or
    $report.passiveCultivationObserved -isnot [bool] -or -not $report.passiveCultivationObserved -or
    $report.automaticKillObserved -isnot [bool] -or -not $report.automaticKillObserved -or
    -not $report.restartMarker.writtenAfterSuccessfulFiveArchiveSave) {
    Fail-Journey ('Native ecology journey report rejected the linked flow or restricted cleanup scope; retained ' + $evidence)
}
Write-Host ('Ecology journey native PASS: ' + $report.elapsedSeconds + ' seconds; ' + $report.nativeClicks + ' clicks; ' + $evidence)
