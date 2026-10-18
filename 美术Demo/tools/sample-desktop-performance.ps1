param(
    [Parameter(Mandatory=$true)][int]$DemoProcessId,
    [Parameter(Mandatory=$true)][string]$OutputCsv,
    [int]$DurationSeconds = 180,
    [int]$IntervalSeconds = 2
)
$ErrorActionPreference = 'Stop'
if ($DurationSeconds -lt 10 -or $DurationSeconds -gt 900 -or $IntervalSeconds -lt 1) { throw 'Invalid sampling duration or interval.' }
$demoProcess = Get-Process -Id $DemoProcessId
if ([IO.Path]::GetFileName($demoProcess.Path) -ne '桌面修仙-透明桌面Demo.exe') { throw 'Only the isolated desktop Demo may be sampled.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DesktopDemoGuiResources {
 [DllImport("user32.dll")] public static extern int GetGuiResources(IntPtr process, int flag);
}
'@
$records = New-Object System.Collections.Generic.List[object]
$watch = [Diagnostics.Stopwatch]::StartNew()
$priorTime = 0.0
$priorCpu = $demoProcess.TotalProcessorTime.TotalSeconds
while ($watch.Elapsed.TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Seconds $IntervalSeconds
    try { $demoProcess.Refresh(); if ($demoProcess.HasExited) { break } } catch { break }
    $seconds = $watch.Elapsed.TotalSeconds
    $cpu = $demoProcess.TotalProcessorTime.TotalSeconds
    $oneCorePercent = 100.0 * ($cpu - $priorCpu) / [Math]::Max(0.001, $seconds - $priorTime)
    $records.Add([pscustomobject]@{
        utc=[DateTime]::UtcNow.ToString('o'); seconds=[Math]::Round($seconds,2)
        cpuOneCorePercent=[Math]::Round($oneCorePercent,2)
        cpuMachinePercent=[Math]::Round($oneCorePercent/[Environment]::ProcessorCount,3)
        privateMb=[Math]::Round($demoProcess.PrivateMemorySize64/1MB,2)
        workingSetMb=[Math]::Round($demoProcess.WorkingSet64/1MB,2)
        handles=$demoProcess.HandleCount
        gdi=[DesktopDemoGuiResources]::GetGuiResources($demoProcess.Handle,0)
        user=[DesktopDemoGuiResources]::GetGuiResources($demoProcess.Handle,1)
    })
    $priorTime = $seconds; $priorCpu = $cpu
}
$outputFullPath = [IO.Path]::GetFullPath($OutputCsv)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
$records | Export-Csv -LiteralPath $outputFullPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{ samples=$records.Count; seconds=[Math]::Round($watch.Elapsed.TotalSeconds,1); requestedSeconds=$DurationSeconds; processId=$DemoProcessId } | ConvertTo-Json
