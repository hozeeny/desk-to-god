param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$executable = Join-Path $OutputDirectory 'ResourceReviewProbe.exe'
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | ForEach-Object FullName)
$sources += Join-Path $PSScriptRoot 'ResourceReviewProbe.cs'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/codepage:65001', '/main:ResourceReviewProbe',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Runtime.Serialization.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $executable)) + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '资源边界验证工具编译失败。' }
& $executable (Join-Path $sourceRoot 'Assets')
if ($LASTEXITCODE -ne 0) { throw '资源边界验证失败。' }
