param([ValidateSet('red','green')][string]$Stage = 'green')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = Join-Path (Join-Path $projectRoot '美术Demo\verification\equipment-flow') ($Stage + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$sourceRoot = Join-Path $projectRoot '源代码'
$snapshot = Join-Path $outputRoot 'sources'
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Copy-Item -Destination $snapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopEquipmentFlowTests.cs') -Destination $snapshot
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | ForEach-Object FullName)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $outputRoot 'DesktopEquipmentFlowTests.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopEquipmentFlowTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $exe)) + $sources
& $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Equipment flow fixture did not compile.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($exe + '.config')
& $exe (Join-Path $sourceRoot 'Assets') $outputRoot (Join-Path $sourceRoot 'ArtDemoAssets') 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'checks.log')
$testExit = $LASTEXITCODE
Write-Host ('Verification directory: ' + $outputRoot)
exit $testExit
