param(
    [switch]$Run,
    [switch]$VisibleOwner,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$demoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $demoRoot
$sourceRoot = Join-Path $projectRoot '源代码'
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$buildRoot = Join-Path $demoRoot ('verification\process-native\build-' + $runId)
[IO.Directory]::CreateDirectory($buildRoot) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $buildRoot 'DesktopProcessAcceptance.exe'
$fixture = Join-Path $buildRoot 'DesktopProcessFixture.exe'
$fixtureArguments = @('/nologo', '/target:winexe', '/platform:x64', '/codepage:65001', '/reference:System.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', ('/out:' + $fixture), (Join-Path $PSScriptRoot 'DesktopProcessFixture.cs'))
& $compiler @fixtureArguments
if ($LASTEXITCODE -ne 0) { throw ('Owned fixture compile failed: ' + $LASTEXITCODE) }
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$sources += Join-Path $PSScriptRoot 'DesktopProcessAcceptance.cs'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopProcessAcceptance',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $exe)) + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw ('Native owned-process acceptance compile failed: ' + $LASTEXITCODE) }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($exe + '.config')
Write-Host ('Compiled runner: ' + $exe)
Write-Host ('Compiled owned fixture: ' + $fixture)
if (-not $Run) { Write-Host 'Build only. -Run explicitly requests real mouse input to this runner and its unique fixture. No other GUI test should run simultaneously.'; return }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $demoRoot ('qa\desktop-accept-process-' + $runId) }
$nativeArguments = @((Join-Path $sourceRoot 'Assets'), (Join-Path $sourceRoot 'ArtDemoAssets'), $fixture, [IO.Path]::GetFullPath($OutputDirectory))
if ($VisibleOwner) { $nativeArguments += '--VisibleOwner' }
& $exe @nativeArguments
if ($LASTEXITCODE -ne 0) { throw ('Native owned-process acceptance failed; inspect evidence: ' + $OutputDirectory) }
Write-Host ('Passed: ' + (Join-Path $OutputDirectory 'desktop-process-acceptance.json'))
