param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'runtime-desktop'))
$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '源代码'
$runtimeRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$allowedRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
if (-not $runtimeRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Demo 输出必须位于美术Demo目录内，防止覆盖正式输出。' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows x64 与 .NET Framework 4.8。' }
[System.IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | ForEach-Object FullName)
$demoExe = Join-Path $runtimeRoot '桌面修仙-透明桌面Demo.exe'
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', '/define:DESKTOP_DEMO_ONLY',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),
    ('/win32icon:' + (Join-Path $sourceRoot 'knight.ico')), ('/out:' + $demoExe)) + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw ('Demo 编译失败，退出码 ' + $LASTEXITCODE) }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($demoExe + '.config') -Force
foreach ($name in @('Assets', 'ArtDemoAssets')) {
    $from = Join-Path $sourceRoot $name
    if (-not (Test-Path -LiteralPath $from -PathType Container)) { throw ('缺少素材目录：' + $from) }
    $to = Join-Path $runtimeRoot $name
    [System.IO.Directory]::CreateDirectory($to) | Out-Null
    Get-ChildItem -LiteralPath $from -Force | Copy-Item -Destination $to -Recurse -Force
}
Write-Host ('隔离 Demo 编译完成：' + $demoExe)
Write-Host '使用 launch-demo.ps1 启动；默认直接进入透明桌面；进度仅写入 EXE 同目录 profiles；普通入口只读观察电脑生态，真实维护须在游戏内分别授权；验收与种子入口保持纯虚拟。'
