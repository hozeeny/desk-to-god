param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$demoRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path $demoRoot -Parent
$verificationRoot = [IO.Path]::GetFullPath((Join-Path $demoRoot 'verification')).TrimEnd('\') + '\'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $verificationRoot ('desktop-launcher-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith($verificationRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '验证输出必须位于美术Demo/verification内。' }
if (Test-Path -LiteralPath $outputRoot) { throw '验证输出必须使用全新目录。' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$utf8Bom = New-Object Text.UTF8Encoding($true)
$results = New-Object 'Collections.Generic.List[object]'
$failures = New-Object 'Collections.Generic.List[string]'

# These spies replace only process launch and CIM observation in this script's
# child scopes. No real game, builder, CIM query, user file or process is used.
function Start-Process {
    param([string]$FilePath, [object[]]$ArgumentList, [string]$WorkingDirectory,
        [string]$WindowStyle, [switch]$PassThru, [switch]$Wait)
    $global:DesktopLauncherContract.Starts.Add([pscustomobject]@{
        FilePath = $FilePath; Arguments = @($ArgumentList); WorkingDirectory = $WorkingDirectory
        WindowStyle = $WindowStyle; Wait = [bool]$Wait; PassThru = [bool]$PassThru
    })
    if ($PassThru) { [pscustomobject]@{ ExitCode = $global:DesktopLauncherContract.ProcessExitCode } }
}
function Get-CimInstance {
    param([string]$ClassName, [string]$Filter, [string]$ErrorAction)
    $global:DesktopLauncherContract.CimCalls++
    if ($global:DesktopLauncherContract.CimThrows) { throw 'contract CIM unavailable' }
    @($global:DesktopLauncherContract.Processes)
}
function Check([bool]$Condition, [string]$Message) {
    $script:results.Add([pscustomobject]@{ Case = $script:caseName; Passed = $Condition; Check = $Message })
    if (-not $Condition) { $script:failures.Add($script:caseName + ': ' + $Message) }
}
function New-Fixture([string]$Name, [bool]$HasExecutable = $true) {
    $script:caseName = $Name
    $root = Join-Path $outputRoot ($Name + ' 含空格试玩根')
    $demo = Join-Path $root '美术Demo'
    $runtime = Join-Path $demo 'runtime-desktop'
    [IO.Directory]::CreateDirectory($runtime) | Out-Null
    Copy-Item -LiteralPath (Join-Path $demoRoot 'launch-demo.ps1') -Destination (Join-Path $demo 'launch-demo.ps1')
    foreach ($entry in @('Start-DesktopDemo.ps1', '开始修仙.cmd')) {
        $source = Join-Path $repositoryRoot $entry
        if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination (Join-Path $root $entry) }
    }
    $stub = @'
param([string]$OutputDirectory)
$global:DesktopLauncherContract.BuildCalls++
$runtime = Join-Path $PSScriptRoot 'runtime-desktop'
[IO.Directory]::CreateDirectory($runtime) | Out-Null
[IO.File]::WriteAllText((Join-Path $runtime '桌面修仙-透明桌面Demo.exe'), 'contract-only executable')
'@
    [IO.File]::WriteAllText((Join-Path $demo 'build-desktop-demo.ps1'), $stub, $utf8Bom)
    $exe = Join-Path $runtime '桌面修仙-透明桌面Demo.exe'
    if ($HasExecutable) { [IO.File]::WriteAllText($exe, 'contract-only executable') }
    $global:DesktopLauncherContract = [pscustomobject]@{
        BuildCalls = 0; CimCalls = 0; Processes = @(); CimThrows = $false; ProcessExitCode = 0
        Starts = (New-Object 'Collections.Generic.List[object]')
    }
    [pscustomobject]@{ Root = $root; Demo = $demo; Runtime = $runtime; Exe = $exe; Launcher = (Join-Path $demo 'launch-demo.ps1') }
}
function Invoke-Launcher([string]$Path, [hashtable]$Parameters = @{}) {
    $script:lastError = $null
    try { & $Path @Parameters | Out-Null }
    catch { $script:lastError = $_.Exception.Message }
}
function Check-OrdinaryStart($Fixture, [string]$Profile = 'player') {
    Check ($global:DesktopLauncherContract.Starts.Count -eq 1) 'exactly one process launch'
    if ($global:DesktopLauncherContract.Starts.Count -ne 1) { return }
    $start = $global:DesktopLauncherContract.Starts[0]
    Check ($start.FilePath -ceq $Fixture.Exe) 'launches exact fixture executable'
    Check ($start.WorkingDirectory -ceq $Fixture.Runtime) 'uses runtime working directory'
    Check (($start.Arguments -join '|') -ceq ('--desktop-demo|--profile|' + $Profile)) 'ordinary entry has only desktop-demo and private profile arguments'
    Check ($start.WindowStyle -eq 'Normal' -and -not $start.Wait) 'ordinary launch is visibly interactive and does not wait'
}

$f = New-Fixture 'existing-default'
Invoke-Launcher $f.Launcher
Check ($null -eq $lastError) 'existing runtime launches successfully'
Check ($global:DesktopLauncherContract.BuildCalls -eq 0) 'existing runtime is reused without rebuilding'
Check ($global:DesktopLauncherContract.CimCalls -eq 0) 'ordinary reuse does not query processes'
Check-OrdinaryStart $f

$f = New-Fixture 'missing-default' $false
Invoke-Launcher $f.Launcher
Check ($null -eq $lastError) 'missing runtime builds and launches'
Check ($global:DesktopLauncherContract.BuildCalls -eq 1) 'missing runtime builds exactly once'
Check-OrdinaryStart $f

$f = New-Fixture 'missing-skip' $false
Invoke-Launcher $f.Launcher @{ SkipBuild = $true }
Check ($null -ne $lastError) 'SkipBuild with missing runtime is rejected'
Check ($global:DesktopLauncherContract.BuildCalls -eq 0 -and $global:DesktopLauncherContract.Starts.Count -eq 0) 'SkipBuild rejection has no build or launch'

$f = New-Fixture 'existing-skip'
Invoke-Launcher $f.Launcher @{ SkipBuild = $true; Profile = 'fixture_2' }
Check ($null -eq $lastError -and $global:DesktopLauncherContract.BuildCalls -eq 0) 'existing SkipBuild remains compatible'
Check-OrdinaryStart $f 'fixture_2'

$f = New-Fixture 'explicit-rebuild'
Invoke-Launcher $f.Launcher @{ Rebuild = $true }
Check ($null -eq $lastError) 'explicit Rebuild succeeds without an active matching process'
Check ($global:DesktopLauncherContract.BuildCalls -eq 1 -and $global:DesktopLauncherContract.CimCalls -eq 1) 'explicit Rebuild checks processes and builds exactly once'
Check-OrdinaryStart $f

$f = New-Fixture 'conflicting-switches'
Invoke-Launcher $f.Launcher @{ Rebuild = $true; SkipBuild = $true }
Check ($null -ne $lastError) 'Rebuild and SkipBuild cannot be combined'
Check ($global:DesktopLauncherContract.BuildCalls -eq 0 -and $global:DesktopLauncherContract.CimCalls -eq 0 -and $global:DesktopLauncherContract.Starts.Count -eq 0) 'conflict is rejected before build, query or launch'

foreach ($badProfile in @('../player', 'bad profile', '', ('a' * 65))) {
    $f = New-Fixture ('invalid-profile-' + $results.Count) $false
    Invoke-Launcher $f.Launcher @{ Profile = $badProfile }
    Check ($null -ne $lastError) 'invalid profile is rejected'
    Check ($global:DesktopLauncherContract.BuildCalls -eq 0 -and $global:DesktopLauncherContract.CimCalls -eq 0 -and $global:DesktopLauncherContract.Starts.Count -eq 0) 'invalid profile is rejected before build, query or launch'
}

$f = New-Fixture 'invalid-normal-speed' $false
Invoke-Launcher $f.Launcher @{ NormalSpeed = $true }
Check ($null -ne $lastError) 'NormalSpeed without explicit acceptance is rejected'
Check ($global:DesktopLauncherContract.BuildCalls -eq 0 -and $global:DesktopLauncherContract.Starts.Count -eq 0) 'NormalSpeed rejection precedes build or launch'

$f = New-Fixture 'invalid-accept-profile' $false
Invoke-Launcher $f.Launcher @{ AcceptOutputDirectory = (Join-Path $f.Root 'accept-output'); Profile = 'player' }
Check ($null -ne $lastError) 'explicit acceptance cannot use ordinary player profile'
Check ($global:DesktopLauncherContract.BuildCalls -eq 0 -and $global:DesktopLauncherContract.Starts.Count -eq 0) 'acceptance profile rejection precedes build or launch'

$f = New-Fixture 'active-same-path'
$global:DesktopLauncherContract.Processes = @([pscustomobject]@{ ExecutablePath = $f.Exe.ToUpperInvariant() })
Invoke-Launcher $f.Launcher @{ Rebuild = $true }
Check ($null -ne $lastError -and $lastError -match '托盘') 'active same-path rebuild asks for normal tray exit'
Check ($global:DesktopLauncherContract.BuildCalls -eq 0 -and $global:DesktopLauncherContract.Starts.Count -eq 0) 'active same-path process is never overwritten or relaunched'

$f = New-Fixture 'active-other-path'
$global:DesktopLauncherContract.Processes = @([pscustomobject]@{ ExecutablePath = (Join-Path $f.Root 'other/桌面修仙-透明桌面Demo.exe') })
Invoke-Launcher $f.Launcher @{ Rebuild = $true }
Check ($null -eq $lastError -and $global:DesktopLauncherContract.BuildCalls -eq 1) 'same filename at another absolute path does not block rebuild'
Check-OrdinaryStart $f

$f = New-Fixture 'query-unavailable'
$global:DesktopLauncherContract.CimThrows = $true
Invoke-Launcher $f.Launcher @{ Rebuild = $true }
Check ($null -ne $lastError) 'unavailable process verification refuses rebuild'
Check ($global:DesktopLauncherContract.BuildCalls -eq 0 -and $global:DesktopLauncherContract.Starts.Count -eq 0) 'failed process verification has no build or launch'

$f = New-Fixture 'acceptance-compatible'
$acceptOutput = Join-Path $f.Root 'accept output'
Invoke-Launcher $f.Launcher @{ SkipBuild = $true; AcceptOutputDirectory = $acceptOutput; Profile = 'verify-launcher'; Seed = 'zhuji'; NormalSpeed = $true }
Check ($null -eq $lastError -and $global:DesktopLauncherContract.BuildCalls -eq 0) 'explicit virtual acceptance parameters remain compatible'
Check ($global:DesktopLauncherContract.Starts.Count -eq 1) 'acceptance launches exactly once'
if ($global:DesktopLauncherContract.Starts.Count -eq 1) {
    $start = $global:DesktopLauncherContract.Starts[0]
    Check (($start.Arguments -join '|') -ceq ('--desktop-demo-accept|"' + $acceptOutput + '"|--profile|verify-launcher|--seed|zhuji|--normal-speed')) 'acceptance arguments retain their explicit virtual entry'
    Check ($start.Wait -and $start.PassThru) 'acceptance still waits and checks exit status'
}

$f = New-Fixture 'root-unicode-path'
$rootEntry = Join-Path $f.Root 'Start-DesktopDemo.ps1'
Check (Test-Path -LiteralPath $rootEntry -PathType Leaf) 'ASCII-named root PowerShell entry exists'
if (Test-Path -LiteralPath $rootEntry -PathType Leaf) {
    Push-Location $outputRoot
    try { Invoke-Launcher $rootEntry }
    finally { Pop-Location }
    Check ($null -eq $lastError -and $global:DesktopLauncherContract.BuildCalls -eq 0) 'root forwards from unrelated working directory in a Unicode path with spaces'
    Check-OrdinaryStart $f
}

$script:caseName = 'source-encoding-and-command-wrapper'
foreach ($psFile in @((Join-Path $demoRoot 'launch-demo.ps1'), (Join-Path $repositoryRoot 'Start-DesktopDemo.ps1'), $PSCommandPath)) {
    Check (Test-Path -LiteralPath $psFile -PathType Leaf) ('PowerShell source exists: ' + [IO.Path]::GetFileName($psFile))
    if (Test-Path -LiteralPath $psFile -PathType Leaf) {
        $bytes = [IO.File]::ReadAllBytes($psFile)
        Check ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) ('PowerShell source is UTF8 BOM: ' + [IO.Path]::GetFileName($psFile))
    }
}
$cmdFile = Join-Path $repositoryRoot '开始修仙.cmd'
Check (Test-Path -LiteralPath $cmdFile -PathType Leaf) 'double-click command entry exists'
if (Test-Path -LiteralPath $cmdFile -PathType Leaf) {
    $cmdBytes = [IO.File]::ReadAllBytes($cmdFile)
    Check (@($cmdBytes | Where-Object { $_ -gt 127 }).Count -eq 0) 'command wrapper contains ASCII bytes only'
    $cmdText = [Text.Encoding]::ASCII.GetString($cmdBytes)
    Check ($cmdText.Contains('"%~dp0Start-DesktopDemo.ps1"')) 'command wrapper anchors quoted PowerShell entry to its own directory'
    Check ($cmdText -match '(?im)^if errorlevel 1' -and $cmdText -match '(?im)^\s*exit /b 1') 'command wrapper reports a failure with a nonzero exit'
    Check ($cmdText -notmatch '%\*|--seed|--desktop-demo|runas|Stop-Process|taskkill') 'command wrapper only delegates and does not inject arguments or change programs'
}

$sourceHashes = @{}
foreach ($sourcePath in @((Join-Path $demoRoot 'launch-demo.ps1'), (Join-Path $repositoryRoot 'Start-DesktopDemo.ps1'), $cmdFile, $PSCommandPath)) {
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) { $sourceHashes[[IO.Path]::GetFileName($sourcePath)] = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$summary = [pscustomobject]@{
    Scope = 'Script behavior contract only. Real launcher copies; stub builder, Start-Process and CIM spies; no native GUI, real process query, game launch or user data access.'
    PowerShellVersion = $PSVersionTable.PSVersion.ToString(); Checks = $results.Count; Failed = $failures.Count
    SourceHashes = $sourceHashes; Results = $results.ToArray(); Failures = $failures.ToArray()
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'summary.json'), ($summary | ConvertTo-Json -Depth 8), $utf8Bom)
foreach ($failure in $failures) { Write-Host ('FAIL ' + $failure) }
Write-Host ('Launcher script contract: ' + ($results.Count - $failures.Count) + '/' + $results.Count + ' passed; ' + $failures.Count + ' failed.')
Write-Host ('Evidence: ' + (Join-Path $outputRoot 'summary.json'))
if ($failures.Count -gt 0) { exit 1 }
