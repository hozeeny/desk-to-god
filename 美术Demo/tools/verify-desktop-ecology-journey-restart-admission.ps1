$ErrorActionPreference='Stop'
$taskRepo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskFixtureRoot=Join-Path $taskRepo ('美术Demo/verification/desktop-ecology-journey-native/_restart-admission-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($taskFixtureRoot)|Out-Null
$taskUtf8=New-Object Text.UTF8Encoding($true)
function Write-Fixture($p,$v){[IO.File]::WriteAllText($p,(ConvertTo-Json -InputObject $v -Depth 6),$taskUtf8)}
$taskCases=@('missing-terminal','nonzero-exit','timeout','changed-archives','wrapper-error','terminal-failure','wrong-build','wrong-evidence','missing-exit')
$taskResults=@()
foreach($taskCase in $taskCases){
 $taskBuild=Join-Path $taskFixtureRoot $taskCase
 $taskEvidence=Join-Path $taskBuild 'synthetic-evidence'
 [IO.Directory]::CreateDirectory($taskEvidence)|Out-Null
 Write-Fixture (Join-Path $taskEvidence 'ecology-journey-native.json') ([pscustomobject]@{passed=$true;journeyComplete=$true;allScopesOffAtExit=$true;scope='Synthetic admission-only fixture; no game profile or gameplay outcome.'})
 $taskFormal=@{ChildExitCode=0;TimedOut=$false;Unchanged=$true;Error=$null;BuildRoot=$taskBuild;Evidence=$taskEvidence}
 $taskExpected='The original journey wrapper did not finish successfully'
 switch($taskCase){
  'missing-terminal' {$taskExpected='no terminal isolation result'}
  'nonzero-exit' {$taskFormal.ChildExitCode=1}
  'timeout' {$taskFormal.TimedOut=$true}
  'changed-archives' {$taskFormal.Unchanged=$false}
  'wrapper-error' {$taskFormal.Error='synthetic wrapper exception'}
  'terminal-failure' {Write-Fixture (Join-Path $taskBuild 'terminal-failure.json') @{scope='Synthetic retained failure marker'};$taskExpected='retained terminal failure'}
  'wrong-build' {$taskFormal.BuildRoot=Join-Path $taskFixtureRoot 'another-build';$taskExpected='does not belong'}
  'wrong-evidence' {$taskFormal.Evidence=Join-Path $taskFixtureRoot 'another-evidence';$taskExpected='does not belong'}
  'missing-exit' {$taskFormal.Remove('ChildExitCode')}
 }
 if($taskCase -ne 'missing-terminal'){Write-Fixture (Join-Path $taskBuild 'formal-isolation.json') $taskFormal}
 $taskCaught=$null
 try{& (Join-Path $taskRepo '美术Demo/tools/verify-desktop-ecology-journey-restart.ps1') -SourceBuildRoot $taskBuild -SourceEvidenceRoot $taskEvidence -RunNative -ExpectedExecutableSHA256 ('0'*64)}catch{$taskCaught=$_.Exception.Message}
 $taskNoCopies=-not(Test-Path -LiteralPath (Join-Path $taskBuild '桌面修仙-透明桌面Demo.exe')) -and -not(Test-Path -LiteralPath (Join-Path $taskBuild 'profiles')) -and -not(Test-Path -LiteralPath (Join-Path $taskBuild 'Assets'))
 $taskOkay=$taskCaught -and $taskCaught.Contains($taskExpected) -and $taskNoCopies
 $taskResults+=[pscustomobject]@{case=$taskCase;passed=[bool]$taskOkay;reason=$taskCaught;noExecutableAssetsOrProfileCopied=$taskNoCopies}
}
$taskOutput=Join-Path $taskFixtureRoot 'admission-results.json'
Write-Fixture $taskOutput ([pscustomobject]@{scope='Synthetic terminal-admission contracts only, no process, gameplay, user archive or native input; all false reports rejected before file copying.';checks=$taskResults.Count;failures=@($taskResults|Where-Object {-not $_.passed}).Count;results=$taskResults})
$taskResults|ForEach-Object {Write-Host (('PASS ','FAIL ')[[int](-not $_.passed)]+$_.case+': '+$_.reason)}
Write-Host ('Admission evidence: '+$taskOutput)
if(@($taskResults|Where-Object {-not $_.passed}).Count){exit 1}
