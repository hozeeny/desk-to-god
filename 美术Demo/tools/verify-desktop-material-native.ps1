param([switch]$RunNative)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$buildRoot = Join-Path $projectRoot ('美术Demo/verification/desktop-material-native/' + $stamp)
$snapshot = Join-Path $buildRoot 'sources'
$evidence = Join-Path $projectRoot ('美术Demo/qa/desktop-accept-material-flow-native-' + $stamp)
$utf8 = New-Object Text.UTF8Encoding($true)
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
$production = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name)
$production | Copy-Item -Destination $snapshot
$productionHashes = @($production | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Source=$_.FullName; SHA256=(Get-FileHash -LiteralPath (Join-Path $snapshot $_.Name) -Algorithm SHA256).Hash } })
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopMaterialFlowNative.cs') -Destination $snapshot
[IO.File]::WriteAllText((Join-Path $buildRoot 'production-sources.json'), (ConvertTo-Json -InputObject $productionHashes -Depth 4), $utf8)
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$snapshotHashes = @($sources | ForEach-Object { [pscustomobject]@{ Name=[IO.Path]::GetFileName($_); SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'compiled-sources.json'), (ConvertTo-Json -InputObject $snapshotHashes -Depth 3), $utf8)
$toolHashes = @('DesktopMaterialFlowNative.cs','verify-desktop-material-native.ps1' | ForEach-Object { [pscustomobject]@{ Name=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $buildRoot 'tool-sources.json'), (ConvertTo-Json -InputObject $toolHashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $buildRoot 'DesktopMaterialFlowNative.exe'
$compilerArguments = @('/nologo','/target:exe','/platform:x64','/optimize+','/codepage:65001','/main:NetBunny.DesktopMaterialFlowNative',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll','/reference:Microsoft.CSharp.dll','/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),('/win32icon:' + (Join-Path $sourceRoot 'knight.ico')),('/out:' + $executable)) + $sources
& $compiler @compilerArguments 2>&1 | Tee-Object -FilePath (Join-Path $buildRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Material native fixture compilation failed; no GUI was started.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($executable + '.config')
Write-Host ('Material native build: ' + $buildRoot)
if (-not $RunNative) { Write-Host 'Build only: no process, Form, GUI, profile or native input was started.'; return }

function Read-FormalHashes {
    $root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnightAssistant'
    $hashes = @{}
    if (Test-Path -LiteralPath $root) {
        foreach ($file in Get-ChildItem -LiteralPath $root -File) {
            if ($file.Name -match '^(adventure-v1|cultivation-v2|baiye-v1|lingyu-v1|dongtian-v1|maintenance-consent)\.') {
                $hashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    }
    return $hashes
}
$before = Read-FormalHashes
$profile = Join-Path $buildRoot 'profiles/verify-material-flow-native'
if ((Test-Path -LiteralPath $profile) -or (Test-Path -LiteralPath $evidence)) { throw 'Preserve evidence: native profile and output must be new.' }
$arguments = @((Join-Path $sourceRoot 'Assets'),(Join-Path $sourceRoot 'ArtDemoAssets'),$evidence,$profile)
$quoted = @($arguments | ForEach-Object { '"' + $_ + '"' })
$ownProcess = $null
$timedOut = $false
$childExit = $null
$runError = $null
$runtime = [Diagnostics.Stopwatch]::StartNew()
try {
    # Explicit interactive game child: Normal is essential for the first feature
    # window. A background supervisor may remain Hidden outside this script.
    $ownProcess = Start-Process -FilePath $executable -ArgumentList $quoted -WindowStyle Normal -PassThru `
        -RedirectStandardOutput (Join-Path $buildRoot 'native-stdout.log') -RedirectStandardError (Join-Path $buildRoot 'native-stderr.log')
    $ownHandle = $ownProcess.Handle
    $launch = [pscustomobject]@{ Pid=$ownProcess.Id; StartTimeUtc=$ownProcess.StartTime.ToUniversalTime().ToString('o'); Executable=$executable;
        SHA256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash; Arguments=$arguments; WindowStyle='Normal'; AbsoluteDeadlineSeconds=90;
        Scope='New source-compiled qishi-full x1 UI fixture, not final EXE or natural growth; only this retained child handle can be stopped.' }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'launch.json'), (ConvertTo-Json -InputObject $launch -Depth 4), $utf8)
    while (-not $ownProcess.WaitForExit([Math]::Max(1, [Math]::Min(500, [int](90000 - $runtime.ElapsedMilliseconds))))) {
        if ($runtime.Elapsed.TotalSeconds -ge 90) {
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
}
if ($runError -or $timedOut -or -not $same -or $null -eq $childExit -or $childExit -ne 0) { throw ('Owned native material fixture failed; retained ' + $buildRoot + ' and ' + $evidence) }
$reportPath = Join-Path $evidence 'material-flow-native.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw ('Native material report is missing; retained ' + $evidence) }
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $report.passed -or -not $report.realArchivesUnchanged) { throw ('Native material report rejected the flow; retained ' + $evidence) }
Write-Host ('Material native PASS: ' + $report.elapsedSeconds + ' seconds; ' + $report.nativeClicks + ' clicks; ' + $evidence)
