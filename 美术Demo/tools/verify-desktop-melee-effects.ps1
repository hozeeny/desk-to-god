param([ValidateSet('red','green')][string]$Stage = 'green', [string]$SourceSnapshot = '', [string]$BaselineReport = '')
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSource = Join-Path $taskRepo '源代码'
$taskCompileRoot = if ($SourceSnapshot) { [IO.Path]::GetFullPath($SourceSnapshot) } else { $taskSource }
if ($Stage -eq 'green' -and -not $BaselineReport) { throw 'Green verification requires -BaselineReport pointing to the retained pre-fix melee-effects.json.' }
if ($BaselineReport -and -not (Test-Path -LiteralPath $BaselineReport -PathType Leaf)) { throw 'The frozen old-source raster report was not found.' }
$taskOutput = Join-Path $taskRepo ('美术Demo/verification/melee-effects/' + $Stage + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$taskSnapshot = Join-Path $taskOutput 'sources'
[IO.Directory]::CreateDirectory($taskSnapshot) | Out-Null
$taskUtf8 = New-Object Text.UTF8Encoding($true)
function Write-Record($taskPath, $taskValue) { [IO.File]::WriteAllText($taskPath, (ConvertTo-Json -InputObject $taskValue -Depth 10), $taskUtf8) }
$taskProductionNames = @(Get-ChildItem -LiteralPath $taskSource -Filter '*.cs' -File | Sort-Object Name | ForEach-Object Name)
foreach ($taskName in $taskProductionNames) { Copy-Item -LiteralPath (Join-Path $taskCompileRoot $taskName) -Destination $taskSnapshot }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopMeleeEffectTests.cs') -Destination $taskSnapshot
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $taskOutput 'wrapper-snapshot.ps1')
$taskSources = @(Get-ChildItem -LiteralPath $taskSnapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
Write-Record (Join-Path $taskOutput 'sources.json') ([pscustomobject]@{
    Stage = $Stage; SourceSnapshot = $taskCompileRoot
    ProductionSourceCount = $taskProductionNames.Count
    Files = @($taskSources | ForEach-Object { [pscustomobject]@{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
    WrapperSHA256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
})
$taskFrozenBaseline = ''
if ($BaselineReport) {
    $taskFrozenBaseline = Join-Path $taskOutput 'old-source-baseline.json'
    Copy-Item -LiteralPath $BaselineReport -Destination $taskFrozenBaseline
}
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExe = Join-Path $taskOutput 'DesktopMeleeEffectTests.exe'
$taskCompileArgs = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopMeleeEffectTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $taskExe)) + $taskSources
& $taskCompiler @taskCompileArgs 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Melee-effect pure raster fixture did not compile.' }
Copy-Item -LiteralPath (Join-Path $taskSource 'app.config') -Destination ($taskExe + '.config')
$taskRunArgs = @((Join-Path $taskSource 'Assets'), $taskOutput, (Join-Path $taskSource 'ArtDemoAssets'))
if ($taskFrozenBaseline) { $taskRunArgs += $taskFrozenBaseline }
$taskQuoted = @($taskRunArgs | ForEach-Object { '"' + $_ + '"' })
$taskProcess = $null
$taskTimedOut = $false
$taskChildExit = -1
try {
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskQuoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'checks.log') -RedirectStandardError (Join-Path $taskOutput 'stderr.log')
    $taskOwnHandle = $taskProcess.Handle
    Write-Record (Join-Path $taskOutput 'launch.json') ([pscustomobject]@{
        Pid = $taskProcess.Id; Executable = $taskExe; StartTimeUtc = $taskProcess.StartTime.ToUniversalTime().ToString('o')
        SHA256 = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash; Arguments = $taskRunArgs; DeadlineSeconds = 20
        Scope = 'Own pure in-memory raster fixture only; no GUI, input, system census, controller, user archive or real maintenance.'
    })
    $taskTimedOut = -not $taskProcess.WaitForExit(20000)
    if ($taskTimedOut) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
    $taskProcess.WaitForExit(); $taskProcess.Refresh(); $taskChildExit = $taskProcess.ExitCode
}
finally {
    if ($null -ne $taskProcess) { if (-not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }; $taskProcess.Dispose() }
    Write-Record (Join-Path $taskOutput 'execution.json') ([pscustomobject]@{
        TimedOut = $taskTimedOut; ChildExitCode = $taskChildExit; ProductionSourceCount = $taskProductionNames.Count
        BaselineReportSHA256 = if ($taskFrozenBaseline) { (Get-FileHash -LiteralPath $taskFrozenBaseline -Algorithm SHA256).Hash } else { $null }
    })
}
Get-Content -LiteralPath (Join-Path $taskOutput 'checks.log') -Encoding UTF8 | Select-Object -Last 12
Write-Host ('Verification directory: ' + $taskOutput)
if ($taskTimedOut -or $taskChildExit -ne 0) { exit 1 }
exit 0
