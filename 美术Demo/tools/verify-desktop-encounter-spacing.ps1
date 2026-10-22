param([ValidateSet('red','green')][string]$Stage = 'green', [string]$Assets = '', [string]$Evidence = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
if ([string]::IsNullOrWhiteSpace($Assets)) { $Assets = Join-Path $sourceRoot 'Assets' }
if ([string]::IsNullOrWhiteSpace($Evidence)) { $Evidence = Join-Path (Join-Path $projectRoot '美术Demo\verification\encounter-spacing') ($Stage + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$runRoot = [IO.Path]::GetFullPath($Evidence)
$snapshot = Join-Path $runRoot 'sources'
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Copy-Item -Destination $snapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DesktopEncounterSpacingTests.cs') -Destination $snapshot
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$utf8 = New-Object Text.UTF8Encoding($true)
$hashes = @($sources | ForEach-Object { [pscustomobject]@{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $runRoot 'sources.json'), (ConvertTo-Json -InputObject $hashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $runRoot 'DesktopEncounterSpacingTests.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.DesktopEncounterSpacingTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $exe)) + $sources
& $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Encounter spacing fixture did not compile.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($exe + '.config')
& $exe ([IO.Path]::GetFullPath($Assets)) (Join-Path $runRoot 'private-fixtures') 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'checks.log')
$testExit = $LASTEXITCODE
Write-Host ('Verification directory: ' + $runRoot)
exit $testExit
