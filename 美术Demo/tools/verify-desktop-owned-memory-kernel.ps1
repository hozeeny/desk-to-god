param([switch]$RunKernel, [string]$Evidence = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '美术Demo\verification\owned-memory-kernel'))
if ([string]::IsNullOrWhiteSpace($Evidence)) { $Evidence = Join-Path $evidenceRoot ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + $(if ($RunKernel) { '-kernel' } else { '-build-only' })) }
$runRoot = [IO.Path]::GetFullPath($Evidence)
if (!$runRoot.StartsWith($evidenceRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must remain in the owned-memory-kernel verification directory.' }
if (Test-Path -LiteralPath $runRoot) { throw 'Use a new owned-memory-kernel evidence directory.' }
$snapshot = Join-Path $runRoot 'sources'
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Copy-Item -Destination $snapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopOwnedMemoryKernelTests.cs') -Destination $snapshot
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $runRoot 'wrapper-snapshot.ps1')
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$utf8 = New-Object Text.UTF8Encoding($true)
$hashes = @($sources | ForEach-Object { [pscustomobject]@{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $runRoot 'sources.json'), (ConvertTo-Json -InputObject $hashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $runRoot 'DesktopOwnedMemoryKernelTests.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopOwnedMemoryKernelTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $exe)) + $sources
& $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Owned-memory-kernel fixture did not compile.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($exe + '.config')
$buildReport = [ordered]@{ built = $true; kernelRunRequested = [bool]$RunKernel; kernelRun = $false; actualAPIcalled = $false; executable = $exe; executableSHA256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash; sourceManifestSHA256 = (Get-FileHash -LiteralPath (Join-Path $runRoot 'sources.json') -Algorithm SHA256).Hash; scope = 'Build-phase report only. Only -RunKernel opts into a real Windows API call against one self-created hidden 96 MiB child; see owned-memory-kernel.json for actual calls. No GUI or boss gameplay claim.' }
[IO.File]::WriteAllText((Join-Path $runRoot 'build-report.json'), (ConvertTo-Json -InputObject $buildReport -Depth 4), $utf8)
Write-Host ('Evidence directory: ' + $runRoot)
if (!$RunKernel) { Write-Host 'BUILD ONLY: no test executable or child was started; no kernel API was called.'; exit 0 }

# The only outer timeout target is this wrapper's newly started worker handle.
# Its child also has an independent 22-second exit deadline.
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class OwnedMemoryKernelDeadline { [DllImport("kernel32.dll",SetLastError=true)] public static extern bool TerminateProcess(IntPtr handle,uint code); }'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $exe
$start.Arguments = '--run-kernel "' + $runRoot + '"'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$worker = [Diagnostics.Process]::Start($start)
$retainedWorkerHandle = $worker.Handle
$stdout = $worker.StandardOutput.ReadToEndAsync()
$stderr = $worker.StandardError.ReadToEndAsync()
$outerTimedOut = !$worker.WaitForExit(30000)
if ($outerTimedOut) {
    $terminatedWorker = [OwnedMemoryKernelDeadline]::TerminateProcess($retainedWorkerHandle, 124)
    $terminationError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $worker.WaitForExit(1000) | Out-Null
    $timeoutReport = [ordered]@{ passed = $false; outerDeadlineMilliseconds = 30000; onlyRetainedSelfCreatedWorkerTargeted = $true; workerTerminationSucceeded = $terminatedWorker; terminationWin32Error = $terminationError }
    [IO.File]::WriteAllText((Join-Path $runRoot 'wrapper-timeout.json'), (ConvertTo-Json -InputObject $timeoutReport -Depth 3), $utf8)
}
if ($stdout.Wait(1000)) { [IO.File]::WriteAllText((Join-Path $runRoot 'stdout.log'), $stdout.Result, $utf8); Write-Host $stdout.Result }
if ($stderr.Wait(1000)) { [IO.File]::WriteAllText((Join-Path $runRoot 'stderr.log'), $stderr.Result, $utf8); if ($stderr.Result) { Write-Host $stderr.Result } }
$workerExit = if ($worker.HasExited) { $worker.ExitCode } else { 124 }
$worker.Dispose()
if ($outerTimedOut -or $workerExit -ne 0) { exit 1 }
$resultPath = Join-Path $runRoot 'owned-memory-kernel.json'
if (!(Test-Path -LiteralPath $resultPath)) { throw 'The worker exited without its required native-kernel report.' }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if (!$result.passed -or !$result.actualAPIcalled -or !$result.archivesUnchanged -or !$result.normalStop -or $result.childExitCode -ne 0) { throw 'Native-kernel report did not satisfy the owned-child contract.' }
exit 0
