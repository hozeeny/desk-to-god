param([string]$AcceptOutputDirectory, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build-demo.ps1') }
$runtimeRoot = Join-Path $PSScriptRoot 'runtime'
$demoExe = Join-Path $runtimeRoot '桌面修仙-美术Demo.exe'
if (-not (Test-Path -LiteralPath $demoExe)) { throw '请先运行 build-demo.ps1。' }
$previousAssets = $env:DESK_TO_GOD_ART_DEMO_ASSETS
try {
    $env:DESK_TO_GOD_ART_DEMO_ASSETS = Join-Path $runtimeRoot 'ArtDemoAssets'
    if ($AcceptOutputDirectory) {
        $outputPath = [IO.Path]::GetFullPath($AcceptOutputDirectory)
        # This is the explicitly requested visible, interactive WinForms Demo.
        $process = Start-Process -FilePath $demoExe -ArgumentList @('--art-demo-accept', ('"' + $outputPath + '"')) -WorkingDirectory $runtimeRoot -WindowStyle Normal -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw ('Demo 验收失败，见 ' + $outputPath + ' 与 runtime/art-demo-error.log') }
    } else {
        Start-Process -FilePath $demoExe -ArgumentList '--art-demo' -WorkingDirectory $runtimeRoot -WindowStyle Normal | Out-Null
    }
} finally { $env:DESK_TO_GOD_ART_DEMO_ASSETS = $previousAssets }
