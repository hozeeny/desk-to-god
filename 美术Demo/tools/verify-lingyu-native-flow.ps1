param([switch]$RunNative, [switch]$CrowdProbe)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$outputRoot = Join-Path $projectRoot ('美术Demo\verification\lingyu-native-flow\' + $stamp)
$snapshot = Join-Path $outputRoot 'sources'
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Copy-Item -Destination $snapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LingyuDesktopNative.cs') -Destination $snapshot
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | ForEach-Object FullName)
$utf8 = New-Object Text.UTF8Encoding($true)
$hashes = @($sources | ForEach-Object { [pscustomobject]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $outputRoot 'sources.json'), (ConvertTo-Json -InputObject $hashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $outputRoot 'LingyuDesktopNative.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.LingyuDesktopNative',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')), ('/win32icon:' + (Join-Path $sourceRoot 'knight.ico')), ('/out:' + $executable)) + $sources
& $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Native fixture compilation failed.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($executable + '.config')
Write-Host ('Native verification directory: ' + $outputRoot)
if (-not $RunNative) { return }
$profile = Join-Path $outputRoot 'profiles\verify-lingyu-suspend'
foreach ($phase in @('prepare', 'restore')) {
    $evidence = Join-Path $projectRoot ('美术Demo\qa\desktop-accept-lingyu-suspend-' + $phase + '-' + $stamp)
    $argsForRunner = @((Join-Path $sourceRoot 'Assets'), (Join-Path $sourceRoot 'ArtDemoAssets'), $evidence, $profile, $phase)
    if ($CrowdProbe -and $phase -eq 'restore') { $argsForRunner += 'crowd' }
    $quoted = @($argsForRunner | ForEach-Object { '"' + $_ + '"' })
    $process = Start-Process -FilePath $executable -ArgumentList $quoted -WindowStyle Normal -PassThru -RedirectStandardOutput (Join-Path $outputRoot ($phase + '.log')) -RedirectStandardError (Join-Path $outputRoot ($phase + '-errors.log'))
    $ownHandle = $process.Handle
    [IO.File]::WriteAllText((Join-Path $outputRoot ($phase + '-launch.json')), (ConvertTo-Json -InputObject ([pscustomobject]@{ Pid=$process.Id; Started=$process.StartTime.ToUniversalTime().ToString('o'); Exe=$executable; SHA256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash; Arguments=$argsForRunner }) -Depth 4), $utf8)
    if (-not $process.WaitForExit(115000)) {
        # This is the retained handle to our newly launched child, never a reused PID.
        $process.Kill(); $process.WaitForExit()
        throw ('Own native fixture exceeded its bounded timeout; retained ' + $evidence)
    }
    $process.WaitForExit(); $process.Refresh()
    $reportFile = Join-Path $evidence 'lingyu-native.json'
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportFile)) { throw ('Native ' + $phase + ' failed; retained ' + $evidence) }
    $report = Get-Content -LiteralPath $reportFile -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $report.passed -or -not $report.realArchivesUnchanged) { throw ('Native report failed; retained ' + $evidence) }
    Write-Host ('Native ' + $phase + ': PASS; ' + $report.elapsedSeconds + ' seconds; ' + $report.nativeClicks + ' clicks; ' + $evidence)
}
