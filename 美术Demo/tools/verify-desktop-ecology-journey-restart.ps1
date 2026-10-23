param([Parameter(Mandatory=$true)][string]$SourceBuildRoot,[Parameter(Mandatory=$true)][string]$SourceEvidenceRoot,[switch]$RunNative,[string]$ExpectedExecutableSHA256='')
$ErrorActionPreference='Stop'
if(-not $RunNative){Write-Host 'No process, profile, asset copy or native input is started without -RunNative.';return}
$taskRepo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskDemo=Join-Path $taskRepo '美术Demo'
$taskAllowed=[IO.Path]::GetFullPath((Join-Path $taskDemo 'verification/desktop-ecology-journey-native')).TrimEnd('\')+'\'
$taskBuild=[IO.Path]::GetFullPath($SourceBuildRoot)
$taskEvidence=[IO.Path]::GetFullPath($SourceEvidenceRoot)
if(-not ($taskBuild+'\').StartsWith($taskAllowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Use only the new owned ecology-journey verification build.'}
if(Test-Path -LiteralPath (Join-Path $taskBuild 'terminal-failure.json')){throw 'The original journey has a retained terminal failure; do not accept its intermediate report.'}
$taskTerminalPath=Join-Path $taskBuild 'formal-isolation.json'
if(-not (Test-Path -LiteralPath $taskTerminalPath -PathType Leaf)){throw 'The original journey wrapper has no terminal isolation result.'}
$taskTerminal=Get-Content -LiteralPath $taskTerminalPath -Raw -Encoding UTF8|ConvertFrom-Json
if($taskTerminal.ChildExitCode -ne 0 -or $taskTerminal.TimedOut -isnot [bool] -or $taskTerminal.TimedOut -or
 $taskTerminal.Unchanged -isnot [bool] -or -not $taskTerminal.Unchanged -or -not [string]::IsNullOrEmpty([string]$taskTerminal.Error)){
 throw 'The original journey wrapper did not finish successfully with unchanged formal archives.'
}
if(-not [string]::Equals([IO.Path]::GetFullPath([string]$taskTerminal.BuildRoot),$taskBuild,[StringComparison]::OrdinalIgnoreCase) -or
 -not [string]::Equals([IO.Path]::GetFullPath([string]$taskTerminal.Evidence),$taskEvidence,[StringComparison]::OrdinalIgnoreCase)){
 throw 'The original terminal result does not belong to this build and evidence directory.'
}
$taskRaw=Join-Path $taskEvidence 'ecology-journey-native.json'
$taskSource=Get-Content -LiteralPath $taskRaw -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $taskSource.passed -or -not $taskSource.journeyComplete -or -not $taskSource.allScopesOffAtExit){throw 'Do not resume a failed or incomplete journey.'}
$taskProfile=[IO.Path]::GetFullPath([string]$taskSource.profile)
$taskProfiles=[IO.Path]::GetFullPath((Join-Path $taskBuild 'profiles')).TrimEnd('\')+'\'
$taskProfileName=Split-Path -Leaf $taskProfile
if(-not $taskProfile.StartsWith($taskProfiles,[StringComparison]::OrdinalIgnoreCase) -or -not $taskProfileName.StartsWith('verify-')){throw 'The exact original private profile path must stay inside this build.'}
$taskMarkerPath=Join-Path $taskProfile 'acceptance-expected-restart.json'
if((Get-FileHash -LiteralPath $taskMarkerPath -Algorithm SHA256).Hash -ne [string]$taskSource.restartMarker.sha256){throw 'Original read-only state marker hash changed.'}
$taskMarker=Get-Content -LiteralPath $taskMarkerPath -Raw -Encoding UTF8|ConvertFrom-Json
if(-not [string]::Equals([string]$taskMarker.profile,$taskProfile,[StringComparison]::OrdinalIgnoreCase)){throw 'Original marker profile mismatch.'}
$taskOriginal=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$taskMarker.processId)
if($taskOriginal -and [string]::Equals([string]$taskOriginal.ExecutablePath,(Join-Path $taskBuild 'DesktopEcologyJourneyNative.exe'),[StringComparison]::OrdinalIgnoreCase)){throw 'The original owned journey is still running. Do not start a second session.'}
$taskExeName='桌面修仙-透明桌面Demo.exe'
$taskRuntime=Join-Path $taskDemo 'runtime-desktop'
$taskExpected=$ExpectedExecutableSHA256
if(-not $taskExpected){$taskExpected=[string](Get-Content -LiteralPath (Join-Path $taskDemo 'desktop-delivery.json') -Raw -Encoding UTF8|ConvertFrom-Json).executable_sha256}
if($taskExpected -notmatch '^[a-fA-F0-9]{64}$'){throw 'An exact delivered executable SHA256 is required.'}
$taskExpected=$taskExpected.ToLowerInvariant()
$taskExe=Join-Path $taskBuild $taskExeName
if(Test-Path -LiteralPath $taskExe){throw 'Preserve a previous exact-EXE restart attempt; use its retained outcome.'}
if((Get-FileHash -LiteralPath (Join-Path $taskRuntime $taskExeName) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected){throw 'Current shipped runtime hash changed; re-audit before using it.'}
foreach($taskAssets in @('Assets','ArtDemoAssets')){if(Test-Path -LiteralPath (Join-Path $taskBuild $taskAssets)){throw 'Do not overwrite existing build assets.'}}
Copy-Item -LiteralPath (Join-Path $taskRuntime $taskExeName) -Destination $taskExe
Copy-Item -LiteralPath (Join-Path $taskRuntime ($taskExeName+'.config')) -Destination ($taskExe+'.config')
foreach($taskAssets in @('Assets','ArtDemoAssets')){
 $taskDestination=Join-Path $taskBuild $taskAssets
 Copy-Item -LiteralPath (Join-Path $taskRuntime $taskAssets) -Destination $taskDestination -Recurse
}
$taskOutput=Join-Path $taskDemo ('qa/desktop-accept-ecology-journey-restart-'+(Split-Path -Leaf $taskBuild))
if(Test-Path -LiteralPath $taskOutput){throw 'Preserve previous restart evidence.'}
$taskUtf8=New-Object Text.UTF8Encoding($true)
function Write-Record($p,$v){[IO.File]::WriteAllText($p,(ConvertTo-Json -InputObject $v -Depth 8),$taskUtf8)}
function Read-Formal{
 $r=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnightAssistant';$h=@{}
 if(Test-Path -LiteralPath $r){foreach($f in Get-ChildItem -LiteralPath $r -File){if($f.Name -match '^(adventure-v1|cultivation-v2|baiye-v1|lingyu-v1|dongtian-v1|maintenance-consent)\.'){$h[$f.Name]=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash}}};return $h
}
$taskBefore=Read-Formal
$taskPrivateBefore=@{}
foreach($taskName in @('adventure-v1.txt','cultivation-v2.txt','baiye-v1.txt','lingyu-v1.txt','dongtian-v1.txt','maintenance-consent.txt')){$taskPrivateBefore[$taskName]=(Get-FileHash -LiteralPath (Join-Path $taskProfile $taskName) -Algorithm SHA256).Hash}
$taskArgs=@('--desktop-demo-accept',$taskOutput,'--profile',$taskProfileName,'--normal-speed')
$taskQuoted=@($taskArgs|ForEach-Object{'"'+$_+'"'})
$taskOwned=$null;$taskTimeout=$false;$taskExit=-1
try{
 $taskOwned=Start-Process -FilePath $taskExe -ArgumentList $taskQuoted -WindowStyle Normal -PassThru -RedirectStandardOutput (Join-Path $taskBuild 'exact-restart-stdout.log') -RedirectStandardError (Join-Path $taskBuild 'exact-restart-stderr.log')
 $taskHandle=$taskOwned.Handle
 if($taskOwned.Id -eq [int]$taskMarker.processId){throw 'Do not claim new-process restoration using the original PID.'}
 Write-Record (Join-Path $taskBuild 'exact-restart-launch.json') ([pscustomobject]@{Pid=$taskOwned.Id;Executable=$taskExe;StartTimeUtc=$taskOwned.StartTime.ToUniversalTime().ToString('o');SHA256=(Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash;Arguments=$taskArgs;Profile=$taskProfile;SourceMarker=$taskMarker;PrivateArchiveHashesBefore=$taskPrivateBefore;Scope='Exact shipped EXE in a real new process, same original profile path and unedited source marker; normal x1 official acceptance entry has Virtual safety cap, not ordinary Observe or a repeated maintenance test.'})
 Write-Host ('Exact shipped EXE restart launched, own PID '+$taskOwned.Id)
 $taskTimeout=-not $taskOwned.WaitForExit(90000)
 if($taskTimeout){$taskOwned.Kill();$taskOwned.WaitForExit()}
 $taskOwned.WaitForExit();$taskOwned.Refresh();$taskExit=$taskOwned.ExitCode
}finally{
 if($null -ne $taskOwned){if(-not $taskOwned.HasExited){$taskOwned.Kill();$taskOwned.WaitForExit()};$taskOwned.Dispose()}
 $taskAfter=Read-Formal;$taskSame=$taskBefore.Count -eq $taskAfter.Count
 foreach($n in $taskBefore.Keys){if(-not $taskAfter.ContainsKey($n) -or $taskBefore[$n] -ne $taskAfter[$n]){$taskSame=$false}}
 Write-Record (Join-Path $taskBuild 'exact-restart-isolation.json') ([pscustomobject]@{Before=$taskBefore;After=$taskAfter;Unchanged=$taskSame;TimedOut=$taskTimeout;ChildExitCode=$taskExit})
}
if($taskTimeout -or -not $taskSame -or $taskExit -ne 0){throw 'Exact shipped EXE restart failed; retain all original evidence.'}
$taskReport=Get-Content -LiteralPath (Join-Path $taskOutput 'desktop-acceptance.json') -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $taskReport.passed -or -not $taskReport.realArchivesUnchanged -or -not @($taskReport.assertions|Where-Object {$_.passed -and $_.detail -match '真正的新进程|新进程恢复'}).Count){throw 'Restart report lacks explicit new-process restoration evidence.'}
$taskReport|Select-Object passed,elapsedSeconds,nativeMouseClicks,realArchivesUnchanged|ConvertTo-Json
