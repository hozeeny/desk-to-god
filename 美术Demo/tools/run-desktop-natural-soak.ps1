param(
    [Parameter(Mandatory=$true)][string]$PrivateProfile,
    [string]$AssetsRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'runtime-desktop\Assets'),
    [string]$ArtRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'runtime-desktop\ArtDemoAssets'),
    [string]$OutputRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) ('qa\desktop-accept-natural-soak-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [double]$Hours = 8,
    [string]$DeliveryExe = (Join-Path (Split-Path $PSScriptRoot -Parent) 'verification\eco-build\桌面修仙-透明桌面Demo.exe'),
    [switch]$BuildOnly
)
$ErrorActionPreference = 'Stop'
if ($Hours -lt 8 -or $Hours -gt 168) { throw 'Hours must be between 8 and 168.' }
$demoRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path (Split-Path $demoRoot -Parent) '源代码'
$verification = Join-Path $demoRoot 'verification\natural-soak'
[IO.Directory]::CreateDirectory($verification) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $verification 'DesktopNaturalSoak.exe'
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | ForEach-Object FullName)
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopNaturalSoakTool',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')), ('/out:' + $executable)) + $sources + (Join-Path $PSScriptRoot 'DesktopNaturalSoak.cs')
& $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $verification 'compile.log')
if ($LASTEXITCODE -ne 0) { throw ('Natural soak compile failed: ' + $LASTEXITCODE) }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($executable + '.config') -Force
& $executable '--self-test' 2>&1 | Tee-Object -FilePath (Join-Path $verification 'self-test.log')
if ($LASTEXITCODE -ne 0) { throw 'Natural soak no-GUI self-test failed.' }
Write-Host ('Built: ' + $executable)
if ($BuildOnly) { return }
if (-not (Test-Path -LiteralPath $DeliveryExe -PathType Leaf)) { throw ('Final delivery EXE missing: ' + $DeliveryExe) }
$profilePath = [IO.Path]::GetFullPath($PrivateProfile)
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
Write-Host ('Running real x1 soak, hours=' + $Hours + '; evidence=' + $outputPath)
& $DeliveryExe '--desktop-demo-soak' ([IO.Path]::GetFullPath($AssetsRoot)) ([IO.Path]::GetFullPath($ArtRoot)) $profilePath $outputPath ($Hours.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($LASTEXITCODE -ne 0) { throw ('Natural soak did not pass. Preserved evidence: ' + $outputPath) }
