[CmdletBinding()]
param([string]$Profile = 'player', [ValidateSet('qishi-full','zhuji')][string]$Seed, [string]$AcceptOutputDirectory, [switch]$SkipBuild, [switch]$NormalSpeed, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
if ($SkipBuild -and $Rebuild) { throw '-SkipBuild 与 -Rebuild 不能同时使用。' }
if ($Profile -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$') { throw '隔离档名只允许字母、数字、下划线和短横线，最长64字。' }
if ($NormalSpeed -and -not $AcceptOutputDirectory) { throw '-NormalSpeed 仅用于显式桌面验收；正常游玩始终为正常速率。' }
if ($AcceptOutputDirectory -and $PSBoundParameters.ContainsKey('Profile') -and $Profile -notmatch '^(accept-|verify-)') { throw '自动验收只能使用 accept- 或 verify- 开头的隔离测试档。' }
$outputPath = if ($AcceptOutputDirectory) { [IO.Path]::GetFullPath($AcceptOutputDirectory) } else { $null }
$runtimeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'runtime-desktop'))
$demoExe = Join-Path $runtimeRoot '桌面修仙-透明桌面Demo.exe'
$hasExecutable = Test-Path -LiteralPath $demoExe -PathType Leaf
if ($SkipBuild -and -not $hasExecutable) { throw '透明桌面运行包不存在；请去掉 -SkipBuild 以构建，或先运行 build-desktop-demo.ps1。' }
if ($Rebuild -or -not $hasExecutable) {
    # Never overwrite a running copy. Only inspect the exact executable path;
    # a game with the same filename in another directory is independent.
    try { $running = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = '桌面修仙-透明桌面Demo.exe'" -ErrorAction Stop) }
    catch { throw ('无法核实透明桌面运行状态，已取消构建。请稍后重试：' + $_.Exception.Message) }
    foreach ($process in $running) {
        if ([string]::IsNullOrWhiteSpace($process.ExecutablePath)) { continue }
        try { $runningPath = [IO.Path]::GetFullPath($process.ExecutablePath) }
        catch { continue }
        if ([string]::Equals($runningPath, $demoExe, [StringComparison]::OrdinalIgnoreCase)) {
            throw '此透明桌面Demo正在运行，已取消构建。请先从托盘选择「保存并退出」，再重新运行。'
        }
    }
    & (Join-Path $PSScriptRoot 'build-desktop-demo.ps1')
}
if (-not (Test-Path -LiteralPath $demoExe -PathType Leaf)) { throw '未找到透明桌面运行包，请运行 build-desktop-demo.ps1。' }
if ($AcceptOutputDirectory) {
    $acceptArgs = @('--desktop-demo-accept', ('"' + $outputPath + '"'))
    if ($PSBoundParameters.ContainsKey('Profile')) { $acceptArgs += @('--profile', $Profile) }
    if ($Seed) { $acceptArgs += @('--seed', $Seed) }
    if ($NormalSpeed) { $acceptArgs += '--normal-speed' }
    $process = Start-Process -FilePath $demoExe -ArgumentList $acceptArgs -WorkingDirectory $runtimeRoot -WindowStyle Normal -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw ('透明桌面验收未通过，请查看 ' + $outputPath + ' 与 runtime-desktop/desktop-demo-error.log') }
} else {
    $arguments = @('--desktop-demo', '--profile', $Profile)
    if ($Seed) { $arguments += @('--seed', $Seed) }
    # User explicitly requested an interactive transparent desktop game.
    Start-Process -FilePath $demoExe -ArgumentList $arguments -WorkingDirectory $runtimeRoot -WindowStyle Normal | Out-Null
}
