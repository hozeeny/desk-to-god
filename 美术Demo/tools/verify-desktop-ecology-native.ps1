param(
    [switch]$Run,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$demoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $demoRoot
$sourceRoot = Join-Path $projectRoot '源代码'
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$buildRoot = Join-Path $demoRoot ('verification\eco-observe-native\build-' + $runId)
[IO.Directory]::CreateDirectory($buildRoot) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $buildRoot 'DesktopEcologyAcceptance.exe'
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$sources += Join-Path $PSScriptRoot 'DesktopEcologyAcceptance.cs'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopEcologyAcceptance',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $exe)) + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw ('Native ecology acceptance compile failed: ' + $LASTEXITCODE) }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($exe + '.config')
Write-Host ('Compiled: ' + $exe)
if (-not $Run) { Write-Host 'Build only. Use -Run explicitly when other desktop acceptance windows have exited.'; return }

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $demoRoot ('qa\desktop-accept-eco-observe-' + $runId) }
$nativeArguments = @((Join-Path $sourceRoot 'Assets'), (Join-Path $sourceRoot 'ArtDemoAssets'), [IO.Path]::GetFullPath($OutputDirectory))
# The runner enforces a new, gitignored raw-output directory, creates a private
# profile under verification, and owns/saves/exits only its own UI process.
& $exe @nativeArguments
if ($LASTEXITCODE -ne 0) { throw ('Native Observe acceptance failed; inspect raw evidence: ' + $OutputDirectory) }
Write-Host ('Passed: ' + (Join-Path $OutputDirectory 'desktop-ecology-acceptance.json'))
