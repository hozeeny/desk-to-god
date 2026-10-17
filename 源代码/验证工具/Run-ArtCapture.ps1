param(
    [switch]$SelfTestOnly,
    [ValidateRange(0,300)][int]$HoldSeconds = 0
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$sourceRoot = Join-Path $repoRoot '源代码'
$runRoot = Join-Path $repoRoot ('artifacts\art-capture\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$binRoot = Join-Path $runRoot 'bin'
[IO.Directory]::CreateDirectory($binRoot) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework x64 compiler unavailable.' }
$probeSource = Join-Path $PSScriptRoot 'ArtCaptureProbe.cs'
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | ForEach-Object FullName) + @($probeSource)
$exe = Join-Path $binRoot 'ArtCaptureProbe.exe'
$compileArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/main:NetBunny.ArtCaptureProbe',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll','/reference:Microsoft.CSharp.dll','/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),('/out:' + $exe)) + $sourceFiles
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw ('Capture probe build failed: ' + $LASTEXITCODE) }
$sourceHashes = @($sourceFiles | ForEach-Object { $h=Get-FileHash -LiteralPath $_ -Algorithm SHA256; @{ path=$h.Path; sha256=$h.Hash } })
$metadata = @{ head=(& git -C $repoRoot rev-parse HEAD); branch=(& git -C $repoRoot branch --show-current); sourceMode='working-tree'; sources=$sourceHashes; executableSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash }
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'build-identity.json') -Encoding UTF8
$assets = Join-Path $sourceRoot 'Assets'
foreach($mode in @('selftest','capture')) {
    if ($mode -eq 'capture' -and $SelfTestOnly) { break }
    $stdout = Join-Path $runRoot ($mode + '.stdout.log')
    $stderr = Join-Path $runRoot ($mode + '.stderr.log')
    $arguments = if($mode -eq 'selftest') { @('--selftest', ('"' + $assets + '"')) } else { @('--capture', ('"' + $assets + '"'), ('"' + $runRoot + '"'), $HoldSeconds.ToString()) }
    $probeProcess = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    if (-not $probeProcess.WaitForExit(($HoldSeconds + 90) * 1000)) { throw ('Capture probe timeout; inspect only its PID ' + $probeProcess.Id + '. No processes were killed.') }
    Get-Content -LiteralPath $stdout
    Get-Content -LiteralPath $stderr
    if ($probeProcess.ExitCode -ne 0) { throw ($mode + ' failed: ' + $probeProcess.ExitCode + '; see ' + $runRoot) }
}
Write-Output ('ART_CAPTURE_OUTPUT=' + $runRoot)
