param([ValidateSet('red', 'green')][string]$Stage = 'green')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourceRoot = Join-Path $projectRoot '源代码'
$runRoot = Join-Path (Join-Path $projectRoot '美术Demo\verification\lingyu-realm-gates') ($Stage + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$snapshot = Join-Path $runRoot 'sources'
[IO.Directory]::CreateDirectory($snapshot) | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Copy-Item -Destination $snapshot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LingyuRealmGateTests.cs') -Destination $snapshot
$sources = @(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$utf8 = New-Object Text.UTF8Encoding($true)
$hashes = @($sources | ForEach-Object { [pscustomobject]@{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText((Join-Path $runRoot 'sources.json'), (ConvertTo-Json -InputObject $hashes -Depth 3), $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $runRoot 'LingyuRealmGateTests.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', '/main:NetBunny.LingyuRealmGateTests', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $executable)) + $sources
& $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'The pure Lingyu realm-gate fixture did not compile.' }
& $executable 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'checks.log')
$testExit = $LASTEXITCODE
Write-Host ('Verification directory: ' + $runRoot)
exit $testExit
