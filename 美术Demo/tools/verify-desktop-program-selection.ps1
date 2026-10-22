param([ValidateSet('red','green')][string]$Stage = 'green', [string]$Evidence = '')
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSource = Join-Path $taskRepo '源代码'
if ([string]::IsNullOrWhiteSpace($Evidence)) { $Evidence = Join-Path $taskRepo ('美术Demo/verification/program-selection/' + $Stage + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$taskOutput = [IO.Path]::GetFullPath($Evidence)
if (Test-Path -LiteralPath $taskOutput) { throw 'Program selection evidence must use a new directory.' }
$taskSnapshot = Join-Path $taskOutput 'sources'
[IO.Directory]::CreateDirectory($taskSnapshot) | Out-Null
$taskUtf8 = New-Object Text.UTF8Encoding($true)
function Write-Record($taskPath, $taskValue) { [IO.File]::WriteAllText($taskPath, (ConvertTo-Json -InputObject $taskValue -Depth 10), $taskUtf8) }
Get-ChildItem -LiteralPath $taskSource -Filter '*.cs' -File | Copy-Item -Destination $taskSnapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopProgramSelectionTests.cs') -Destination $taskSnapshot
Copy-Item -LiteralPath $PSCommandPath -Destination $taskSnapshot
$taskPanelSnapshot = Join-Path $taskSnapshot 'DesktopEcologyPanel.cs'
$taskPanelText = [IO.File]::ReadAllText($taskPanelSnapshot)
$taskSnapshotSeam = $false
if ($Stage -eq 'red' -and -not $taskPanelText.Contains('Func<ProcessIdentity[]>')) {
    # Baseline has no injection seam. Adapt ONLY its observation dependency in
    # the frozen compilation copy; retain every original selection/UI branch.
    # The production file is never written by this wrapper.
    $taskOriginal = $taskPanelText
    $taskPanelText = $taskPanelText.Replace('readonly AdventureController game;', 'readonly AdventureController game; readonly Func<ProcessIdentity[]> observePrograms;')
    $taskPanelText = $taskPanelText.Replace('internal DesktopEcologyPanel(AdventureController game)', 'internal DesktopEcologyPanel(AdventureController game, Func<ProcessIdentity[]> observePrograms = null)')
    $taskPanelText = $taskPanelText.Replace('this.game=game;', 'this.game=game; this.observePrograms=observePrograms??DesktopProcessTargets.Observe;')
    $taskPanelText = $taskPanelText.Replace('Task.Run(()=>DesktopProcessTargets.Observe())', 'Task.Run(this.observePrograms)')
    if ($taskPanelText -eq $taskOriginal -or -not $taskPanelText.Contains('Task.Run(this.observePrograms)')) { throw 'Baseline observation seam did not apply.' }
    [IO.File]::WriteAllText($taskPanelSnapshot, $taskPanelText, $taskUtf8)
    $taskSnapshotSeam = $true
}
$taskSources = @(Get-ChildItem -LiteralPath $taskSnapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
Write-Record (Join-Path $taskOutput 'sources.json') ([pscustomobject]@{ Stage = $Stage; BaselineSnapshotObservationSeam = $taskSnapshotSeam; Files = @($taskSources | ForEach-Object { [pscustomobject]@{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } }); PanelProductionSHA256 = (Get-FileHash -LiteralPath (Join-Path $taskSource 'DesktopEcologyPanel.cs') -Algorithm SHA256).Hash })
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExe = Join-Path $taskOutput 'DesktopProgramSelectionTests.exe'
$taskCompileArgs = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopProgramSelectionTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $taskExe)) + $taskSources
& $taskCompiler @taskCompileArgs 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Program selection fixture did not compile.' }
Copy-Item -LiteralPath (Join-Path $taskSource 'app.config') -Destination ($taskExe + '.config')
$taskRunArgs = @((Join-Path $taskSource 'Assets'), $taskOutput, (Join-Path $taskSource 'ArtDemoAssets'))
$taskQuoted = @($taskRunArgs | ForEach-Object { '"' + $_ + '"' })
$taskProcess = $null
$taskTimedOut = $false
$taskChildExit = -1
try {
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskQuoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'checks.log') -RedirectStandardError (Join-Path $taskOutput 'stderr.log')
    $taskOwnHandle = $taskProcess.Handle
    Write-Record (Join-Path $taskOutput 'launch.json') ([pscustomobject]@{ Pid = $taskProcess.Id; Executable = $taskExe; StartTimeUtc = $taskProcess.StartTime.ToUniversalTime().ToString('o'); SHA256 = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash; Arguments = $taskRunArgs; DeadlineSeconds = 25; Scope = 'Own hidden native Controls only; new private profiles; injected synthetic program observations; no input, real process census, user archive reads or program-close requests.' })
    $taskTimedOut = -not $taskProcess.WaitForExit(25000)
    if ($taskTimedOut) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
    $taskProcess.WaitForExit(); $taskProcess.Refresh(); $taskChildExit = $taskProcess.ExitCode
}
finally {
    if ($null -ne $taskProcess) { if (-not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }; $taskProcess.Dispose() }
    Write-Record (Join-Path $taskOutput 'execution.json') ([pscustomobject]@{ TimedOut = $taskTimedOut; ChildExitCode = $taskChildExit; BaselineSnapshotObservationSeam = $taskSnapshotSeam })
}
Get-Content -LiteralPath (Join-Path $taskOutput 'checks.log') -Encoding UTF8
Write-Host ('Verification directory: ' + $taskOutput)
if ($taskTimedOut -or $taskChildExit -ne 0) { exit 1 }
exit 0
