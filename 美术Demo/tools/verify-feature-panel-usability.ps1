param([ValidateSet('red','green')][string]$Stage = 'green')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = Join-Path (Join-Path $projectRoot '美术Demo\verification\feature-panel-usability') ($Stage + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$sourceRoot = Join-Path $projectRoot '源代码'
$sourceSnapshot = Join-Path $outputRoot 'sources'
[System.IO.Directory]::CreateDirectory($sourceSnapshot) | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Copy-Item -Destination $sourceSnapshot
$testSource = Join-Path $PSScriptRoot 'FeaturePanelUsabilityTests.cs'
Copy-Item -LiteralPath $testSource -Destination $sourceSnapshot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExecutable = Join-Path $outputRoot 'FeaturePanelUsabilityTests.exe'
$sources = @(Get-ChildItem -LiteralPath $sourceSnapshot -Filter '*.cs' -File | ForEach-Object FullName)
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.FeaturePanelUsabilityTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $testExecutable)) + $sources
& $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Feature usability fixture did not compile.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($testExecutable + '.config')
& $testExecutable (Join-Path $sourceRoot 'Assets') $outputRoot (Join-Path $sourceRoot 'ArtDemoAssets') 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'checks.log')
$testExit = $LASTEXITCODE
Write-Host ('Verification directory: ' + $outputRoot)
exit $testExit
