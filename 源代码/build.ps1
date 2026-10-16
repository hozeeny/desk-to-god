param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows x64 和 .NET Framework 4.8。' }
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
if ($sources.Count -eq 0) { throw '没有找到 C# 源代码。' }
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'knight.ico')),
    ('/out:' + (Join-Path $OutputDirectory '骑士助手.exe'))) + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw ('编译失败，退出码 ' + $LASTEXITCODE) }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $OutputDirectory '骑士助手.exe.config') -Force
$assetSource = Join-Path $PSScriptRoot 'Assets'
if (Test-Path -LiteralPath $assetSource -PathType Container) {
    $assetDestination = Join-Path $OutputDirectory 'Assets'
    [System.IO.Directory]::CreateDirectory($assetDestination) | Out-Null
    Get-ChildItem -LiteralPath $assetSource -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $assetDestination -Force
    }
}
Write-Host ('编译完成：' + (Join-Path $OutputDirectory '骑士助手.exe'))
Write-Host '请保留 EXE 旁的 Assets 文件夹，并将官方 WinDivert 2.2.2 的 x64 WinDivert.dll 和 WinDivert64.sys 放在 EXE 同一文件夹。'
