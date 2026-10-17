param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$worktreeRoot = Split-Path -Parent $sourceRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $worktreeRoot ('artifacts\smoke-isolation-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $worktreeRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $OutputDirectory.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Regression output must be inside this worktree artifacts directory.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Regression output directory must be new.' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$controller = Join-Path $sourceRoot 'AdventureController.cs'
$fixtureController = Join-Path $OutputDirectory 'AdventureController.fixture.cs'
$sourceText = [IO.File]::ReadAllText($controller)
$fixtureText = $sourceText
$pathFields = [ordered]@{
    cultivationPath = 'cultivation-v2.txt'
    consentPath = 'maintenance-consent.txt'
    baiyePath = 'baiye-v1.txt'
    lingyuPath = 'lingyu-v1.txt'
    progressPath = 'adventure-v1.txt'
    dongtianPath = 'dongtian-v1.txt'
}
foreach ($field in $pathFields.Keys) {
    $fileName = $pathFields[$field]
    $initializer = 'Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "' + $fileName + '")'
    $declaration = 'private readonly string ' + $field + ' = ' + $initializer + ';'
    $pattern = [regex]::Escape($declaration)
    if ([regex]::Matches($fixtureText, $pattern).Count -ne 1) {
        throw ('Expected exactly one default archive initializer: ' + $field)
    }
    $replacement = 'private readonly string ' + $field + ' = global::SmokeIsolationRegression.ArchivePath("' + $fileName + '");'
    $fixtureText = $fixtureText.Replace($declaration, $replacement)
}
if ($fixtureText.Contains('Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant",')) {
    throw 'An unexpected default archive initializer remains in the generated controller.'
}
[IO.File]::WriteAllText($fixtureController, $fixtureText, (New-Object Text.UTF8Encoding($false)))
$hashes = @(
    'Dependency-root injection: only six default archive initializers are replaced; no smoke assertions/branches are changed.'
    'This harness does not validate an unmodified production binary.'
    ('source ' + (Get-FileHash -LiteralPath $controller -Algorithm SHA256).Hash + ' ' + $controller)
    ('fixture ' + (Get-FileHash -LiteralPath $fixtureController -Algorithm SHA256).Hash + ' ' + $fixtureController)
)
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'source-hashes.txt'), $hashes, (New-Object Text.UTF8Encoding($false)))
$executable = Join-Path $OutputDirectory 'SmokeIsolationRegression.exe'
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File |
    Where-Object Name -ne 'AdventureController.cs' | ForEach-Object FullName)
$sources += $fixtureController
$sources += Join-Path $PSScriptRoot 'SmokeIsolationRegression.cs'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/codepage:65001', '/main:SmokeIsolationRegression',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Runtime.Serialization.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $executable)) + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Smoke isolation regression compilation failed.' }
$log = Join-Path $OutputDirectory 'regression.log'
$errorLog = Join-Path $OutputDirectory 'regression-errors.log'
$processArguments = @(('"' + (Join-Path $sourceRoot 'Assets') + '"'), ('"' + (Join-Path $OutputDirectory 'evidence') + '"'))
$process = Start-Process -FilePath $executable -ArgumentList $processArguments -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError $errorLog
Get-Content -LiteralPath $log
Get-Content -LiteralPath $errorLog
$result = $process.ExitCode
Write-Host ('Regression artifacts: ' + $OutputDirectory)
exit $result
