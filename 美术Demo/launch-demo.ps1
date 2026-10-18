param([string]$Profile = 'player', [ValidateSet('qishi-full','zhuji')][string]$Seed, [string]$AcceptOutputDirectory, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build-desktop-demo.ps1') }
$runtimeRoot = Join-Path $PSScriptRoot 'runtime-desktop'
$demoExe = Join-Path $runtimeRoot '桌面修仙-透明桌面Demo.exe'
if (-not (Test-Path -LiteralPath $demoExe)) { throw '请先运行 build-desktop-demo.ps1。' }
if ($AcceptOutputDirectory) {
    $outputPath = [IO.Path]::GetFullPath($AcceptOutputDirectory)
    $acceptArgs = @('--desktop-demo-accept', ('"' + $outputPath + '"'))
    if ($PSBoundParameters.ContainsKey('Profile')) { $acceptArgs += @('--profile', $Profile) }
    if ($Seed) { $acceptArgs += @('--seed', $Seed) }
    $process = Start-Process -FilePath $demoExe -ArgumentList $acceptArgs -WorkingDirectory $runtimeRoot -WindowStyle Normal -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw ('透明桌面验收未通过，请查看 ' + $outputPath + ' 与 runtime-desktop/desktop-demo-error.log') }
} else {
    if ($Profile -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$') { throw '隔离档名只允许字母、数字、下划线和短横线，最长64字。' }
    $arguments = @('--desktop-demo', '--profile', $Profile)
    if ($Seed) { $arguments += @('--seed', $Seed) }
    # User explicitly requested an interactive transparent desktop game.
    Start-Process -FilePath $demoExe -ArgumentList $arguments -WorkingDirectory $runtimeRoot -WindowStyle Normal | Out-Null
}
