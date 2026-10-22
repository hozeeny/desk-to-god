param([ValidateSet('red','green')][string]$Stage = 'green', [string]$Evidence = '')
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSource = Join-Path $taskRepo '源代码'
if ([string]::IsNullOrWhiteSpace($Evidence)) { $Evidence = Join-Path $taskRepo ('美术Demo/verification/panel-reopen/' + $Stage + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$taskOutput = [IO.Path]::GetFullPath($Evidence)
if (Test-Path -LiteralPath $taskOutput) { throw 'Panel reopen evidence must use a new directory.' }
$taskSnapshot = Join-Path $taskOutput 'sources'
[IO.Directory]::CreateDirectory($taskSnapshot) | Out-Null
$taskUtf8 = New-Object Text.UTF8Encoding($true)
function Write-Record($taskPath, $taskValue) { [IO.File]::WriteAllText($taskPath, (ConvertTo-Json -InputObject $taskValue -Depth 10), $taskUtf8) }
Get-ChildItem -LiteralPath $taskSource -Filter '*.cs' -File | Copy-Item -Destination $taskSnapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopPanelReopenTests.cs') -Destination $taskSnapshot
$taskSources = @(Get-ChildItem -LiteralPath $taskSnapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
Write-Record (Join-Path $taskOutput 'sources.json') @($taskSources | ForEach-Object { [pscustomobject]@{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExe = Join-Path $taskOutput 'DesktopPanelReopenTests.exe'
$taskCompileArgs = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopPanelReopenTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $taskExe)) + $taskSources
& $taskCompiler @taskCompileArgs 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Panel reopen fixture did not compile.' }
Copy-Item -LiteralPath (Join-Path $taskSource 'app.config') -Destination ($taskExe + '.config')
function Read-ProtectedArchives {
    $taskRoots = @((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnightAssistant'), (Join-Path $taskSource 'profiles'), (Join-Path $taskRepo '美术Demo/runtime/profiles'), (Join-Path $taskRepo '美术Demo/runtime-desktop/profiles'))
    $taskHashes = @{}
    foreach ($taskRoot in $taskRoots) {
        if (Test-Path -LiteralPath $taskRoot) {
            foreach ($taskFile in Get-ChildItem -LiteralPath $taskRoot -File -Recurse) {
                if ($taskFile.Name -match '^(adventure-v1|cultivation-v2|baiye-v1|lingyu-v1|dongtian-v1|maintenance-consent)\.') { $taskHashes[$taskFile.FullName] = (Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash }
            }
        }
    }
    return $taskHashes
}
$taskBefore = Read-ProtectedArchives
$taskRunArgs = @((Join-Path $taskSource 'Assets'), $taskOutput, (Join-Path $taskSource 'ArtDemoAssets'))
$taskQuoted = @($taskRunArgs | ForEach-Object { '"' + $_ + '"' })
$taskProcess = $null
$taskTimedOut = $false
$taskChildExit = -1
try {
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskQuoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'checks.log') -RedirectStandardError (Join-Path $taskOutput 'stderr.log')
    $taskOwnHandle = $taskProcess.Handle
    Write-Record (Join-Path $taskOutput 'launch.json') ([pscustomobject]@{ Pid = $taskProcess.Id; Executable = $taskExe; StartTimeUtc = $taskProcess.StartTime.ToUniversalTime().ToString('o'); SHA256 = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash; Arguments = $taskRunArgs; DeadlineSeconds = 25; Scope = 'Own off-screen WinForms fixture only; new private Virtual profile; no input or operations on any other process.' })
    $taskTimedOut = -not $taskProcess.WaitForExit(25000)
    if ($taskTimedOut) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
    $taskProcess.WaitForExit(); $taskProcess.Refresh(); $taskChildExit = $taskProcess.ExitCode
}
finally {
    if ($null -ne $taskProcess) { if (-not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }; $taskProcess.Dispose() }
    $taskAfter = Read-ProtectedArchives
    $taskSame = $taskBefore.Count -eq $taskAfter.Count
    foreach ($taskName in $taskBefore.Keys) { if (-not $taskAfter.ContainsKey($taskName) -or $taskBefore[$taskName] -ne $taskAfter[$taskName]) { $taskSame = $false } }
    Write-Record (Join-Path $taskOutput 'archive-isolation.json') ([pscustomobject]@{ Before = $taskBefore; After = $taskAfter; Count = $taskBefore.Count; Unchanged = $taskSame; TimedOut = $taskTimedOut; ChildExitCode = $taskChildExit })
}
Get-Content -LiteralPath (Join-Path $taskOutput 'checks.log')
Write-Host ('Verification directory: ' + $taskOutput)
if ($taskTimedOut -or -not $taskSame -or $taskChildExit -ne 0) { exit 1 }
exit 0
