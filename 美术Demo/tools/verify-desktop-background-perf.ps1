param([switch]$Run,[string]$SourceSnapshot='',[ValidateSet('both','observe')][string]$Mode='both',[ValidateRange(8,24)][int]$ShortSeconds=8)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
$compileRoot = $sourceRoot
if($SourceSnapshot) { $compileRoot=[IO.Path]::GetFullPath($SourceSnapshot) }
$buildRoot = Join-Path $projectRoot ('美术Demo/verification/background-perf/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$snapshot = Join-Path $buildRoot 'sources'
$utf8 = New-Object Text.UTF8Encoding($true)
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
Get-ChildItem -LiteralPath $compileRoot -Filter '*.cs' -File | Copy-Item -Destination $snapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopBackgroundPerfProbe.cs') -Destination $snapshot
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $buildRoot 'wrapper-snapshot.ps1')
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$hashes = @($sources | ForEach-Object { [pscustomobject]@{Name=[IO.Path]::GetFileName($_); SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} })
[IO.File]::WriteAllText((Join-Path $buildRoot 'sources.json'), (ConvertTo-Json -InputObject $hashes -Depth 3), $utf8)
$exe = Join-Path $buildRoot 'DesktopBackgroundPerfProbe.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$compilerArguments = @('/nologo','/target:exe','/platform:x64','/optimize+','/codepage:65001','/main:NetBunny.DesktopBackgroundPerfProbe','/reference:System.dll','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Runtime.Serialization.dll','/reference:Microsoft.CSharp.dll','/reference:System.Web.Extensions.dll',('/win32manifest:'+(Join-Path $sourceRoot 'app.manifest')),('/out:'+$exe)) + $sources
& $compiler @compilerArguments 2>&1 | Tee-Object -FilePath (Join-Path $buildRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Background probe did not compile; nothing was run.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($exe+'.config')
Write-Host ('Background probe build: '+$buildRoot)
if (!$Run) { Write-Host 'BUILD ONLY; no process, profile, UI or system observation started.'; return }
function Formal-Hashes {
    $root=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnightAssistant'
    $result=@{}
    if(Test-Path -LiteralPath $root) { foreach($file in Get-ChildItem -LiteralPath $root -File) { if($file.Name -match '^(adventure-v1|cultivation-v2|baiye-v1|lingyu-v1|dongtian-v1|maintenance-consent|maintenance-results-v1)\.') { $result[$file.Name]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash } } }
    return $result
}
$before=Formal-Hashes
$runs=@()
try {
    $modes=$(if($Mode -eq 'both') { @('virtual','observe') } else { @('observe') })
    foreach($probeMode in $modes) {
        $output=Join-Path $buildRoot $probeMode
        $start=New-Object Diagnostics.ProcessStartInfo
        $start.FileName=$exe
        $start.Arguments='"'+$probeMode+'" "'+(Join-Path $sourceRoot 'Assets')+'" "'+(Join-Path $sourceRoot 'ArtDemoAssets')+'" "'+$output+'" "'+$ShortSeconds+'"'
        $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
        $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
        $owned=[Diagnostics.Process]::Start($start); $retainedHandle=$owned.Handle
        try {
            $stdout=$owned.StandardOutput.ReadToEndAsync(); $stderr=$owned.StandardError.ReadToEndAsync()
            $deadlineMilliseconds=$(if($ShortSeconds -eq 8) { 15000 } else { ($ShortSeconds+4)*1000 })
            if(!$owned.WaitForExit($deadlineMilliseconds)) { $owned.Kill(); [void]$owned.WaitForExit(1000); throw ('Own '+$probeMode+' probe exceeded its parent deadline.') }
            if($stdout.Wait(1000)) { [IO.File]::WriteAllText((Join-Path $buildRoot ($probeMode+'-stdout.log')),$stdout.Result,$utf8); Write-Host $stdout.Result }
            if($stderr.Wait(1000)) { [IO.File]::WriteAllText((Join-Path $buildRoot ($probeMode+'-stderr.log')),$stderr.Result,$utf8) }
            if($owned.ExitCode -ne 0) { throw ('Own '+$probeMode+' probe failed with exit '+$owned.ExitCode) }
            $runs += Get-Content -LiteralPath (Join-Path $output 'background-perf.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        } finally {
            if(!$owned.HasExited) { $owned.Kill(); [void]$owned.WaitForExit(1000) }
            $owned.Dispose()
        }
    }
} finally {
    $after=Formal-Hashes; $same=$before.Count -eq $after.Count
    foreach($name in $before.Keys) { if(!$after.ContainsKey($name) -or $after[$name] -ne $before[$name]) { $same=$false } }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'comparison.json'),(ConvertTo-Json -InputObject ([ordered]@{formalBefore=$before;formalAfter=$after;formalUnchanged=$same;requestedProcessSeconds=$ShortSeconds;runs=$runs;scope='Hidden actors, fresh private background-only comparison; no raw-gameplay or long-soak claim.'}) -Depth 12),$utf8)
}
if(!$same) { throw 'Formal archive hashes changed during the probe.' }
if($runs.Count -ne $modes.Count -or @($runs|Where-Object { !$_.passed -or $_.forbiddenWorkers -ne 0 }).Count -ne 0) { throw 'All requested short background probes must pass their boundaries.' }
Write-Host ('Comparison evidence: '+$buildRoot)
