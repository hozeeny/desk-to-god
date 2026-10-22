param([switch]$RunNative)
$ErrorActionPreference='Stop'
$taskRepo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSource=Join-Path $taskRepo '源代码'
$taskStamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$taskBuild=Join-Path $taskRepo ('美术Demo/verification/closed-panel-combat-native/'+$taskStamp)
$taskOutput=Join-Path $taskRepo ('美术Demo/qa/desktop-accept-closed-panel-combat-'+$taskStamp)
$taskSnapshot=Join-Path $taskBuild 'sources'
[IO.Directory]::CreateDirectory($taskSnapshot)|Out-Null
Get-ChildItem -LiteralPath $taskSource -Filter '*.cs' -File|Copy-Item -Destination $taskSnapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopCombatFeedbackNative.cs') -Destination $taskSnapshot
$taskSources=@(Get-ChildItem -LiteralPath $taskSnapshot -Filter '*.cs' -File|Sort-Object Name|ForEach-Object FullName)
$taskUtf8=[Text.UTF8Encoding]::new($true)
function Write-Record($taskPath,$taskValue){[IO.File]::WriteAllText($taskPath,(ConvertTo-Json -InputObject $taskValue -Depth 8),$taskUtf8)}
$taskManifest=@($taskSources|ForEach-Object{[pscustomobject]@{Name=[IO.Path]::GetFileName($_);SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
Write-Record (Join-Path $taskBuild 'sources.json') $taskManifest
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExe=Join-Path $taskBuild 'DesktopCombatFeedbackNative.exe'
$taskArgs=@('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/main:NetBunny.DesktopCombatFeedbackNative','/reference:System.dll','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:Microsoft.CSharp.dll','/reference:System.Web.Extensions.dll',('/win32manifest:'+(Join-Path $taskSource 'app.manifest')),('/out:'+$taskExe))+$taskSources
& $taskCompiler @taskArgs 2>&1 | Tee-Object -FilePath (Join-Path $taskBuild 'compile.log')
if($LASTEXITCODE -ne 0){throw 'Closed-panel native runner did not compile'}
Copy-Item -LiteralPath (Join-Path $taskSource 'app.config') -Destination ($taskExe+'.config')
Write-Host ('Closed-panel native build: '+$taskBuild)
if(-not $RunNative){exit 0}
function Read-Formal {
    $taskRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnightAssistant'
    $taskHashes=@{}
    if(Test-Path -LiteralPath $taskRoot){foreach($taskFile in Get-ChildItem -LiteralPath $taskRoot -File){if($taskFile.Name -match '^(adventure-v1|cultivation-v2|baiye-v1|lingyu-v1|dongtian-v1|maintenance-consent)\.'){$taskHashes[$taskFile.Name]=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash}}}
    return $taskHashes
}
$taskBefore=Read-Formal
$taskArguments=@((Join-Path $taskSource 'Assets'),(Join-Path $taskSource 'ArtDemoAssets'),$taskOutput)
$taskQuoted=@($taskArguments|ForEach-Object{'"'+$_+'"'})
$taskProcess=Start-Process -FilePath $taskExe -ArgumentList $taskQuoted -WindowStyle Normal -PassThru -RedirectStandardOutput (Join-Path $taskBuild 'stdout.log') -RedirectStandardError (Join-Path $taskBuild 'stderr.log')
$taskHandle=$taskProcess.Handle
Write-Record (Join-Path $taskBuild 'launch.json') ([pscustomobject]@{Pid=$taskProcess.Id;Executable=$taskExe;StartTimeUtc=$taskProcess.StartTime.ToUniversalTime().ToString('o');SHA256=(Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash;Arguments=$taskArguments;Scope='New self-owned visible short fixture, fresh private Virtual profile; not delivered EXE or long test'})
$taskTimedOut=-not $taskProcess.WaitForExit(55000)
if($taskTimedOut){$taskProcess.Kill();$taskProcess.WaitForExit()}
$taskProcess.WaitForExit();$taskProcess.Refresh()
$taskAfter=Read-Formal
$taskSame=$taskBefore.Count -eq $taskAfter.Count
foreach($taskName in $taskBefore.Keys){if(-not $taskAfter.ContainsKey($taskName) -or $taskBefore[$taskName] -ne $taskAfter[$taskName]){$taskSame=$false}}
Write-Record (Join-Path $taskBuild 'formal-isolation.json') ([pscustomobject]@{Before=$taskBefore;After=$taskAfter;Unchanged=$taskSame;TimedOut=$taskTimedOut;ChildExitCode=$taskProcess.ExitCode})
if($taskTimedOut -or -not $taskSame -or $taskProcess.ExitCode -ne 0){throw ('Closed-panel native failed; retain '+$taskBuild+' and '+$taskOutput)}
$taskReport=Get-Content -LiteralPath (Join-Path $taskOutput 'combat-feedback-native.json') -Raw|ConvertFrom-Json
if(-not $taskReport.passed -or -not $taskReport.closedPanelHud.handbookClosed){throw 'Missing passing native closed-panel HUD evidence'}
Write-Host ('Closed-panel native PASS: '+$taskOutput)
