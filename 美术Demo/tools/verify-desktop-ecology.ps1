param(
    [ValidateSet('Policy', 'Sources', 'Maintenance', 'Processes', 'Cave', 'Journey', 'Equipment', 'Combat', 'Realm', 'AutoBattle', 'Guidance', 'SuspendCore', 'LingyuFlow', 'Spacing', 'ConsentFeedback', 'MaterialGuidance', 'DisplayLayout', 'MemoryOutcomes', 'IdleTargetBadge', 'FinalPathBuffer', 'CensusCadence', 'ReturnTasks', 'CaveSlots', 'PanelReopen', 'All')]
    [string]$Suite = 'Policy',
    [switch]$Baseline
)

$ErrorActionPreference = 'Stop'
$demoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $demoRoot
$sourceRoot = Join-Path $projectRoot '源代码'
$assetRoot = Join-Path $sourceRoot 'Assets'
$outputRoot = [IO.Path]::GetFullPath((Join-Path $demoRoot 'verification\eco-tests'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $demoRoot 'verification')).TrimEnd('\') + '\'
if (-not $outputRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Ecology tests must remain inside the independent verification directory.' }
$runRoot = Join-Path $outputRoot ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$binRoot = Join-Path $runRoot 'bin'
$logRoot = Join-Path $runRoot 'logs'
[IO.Directory]::CreateDirectory($binRoot) | Out-Null
[IO.Directory]::CreateDirectory($logRoot) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Windows x64 and .NET Framework 4.8 csc are required.' }
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
if ($sources.Count -eq 0) { throw 'No production C# source files were found.' }
$utf8 = New-Object Text.UTF8Encoding($true)
$results = New-Object 'System.Collections.Generic.List[object]'

function Add-Result([string]$Name, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail })
    Write-Host ($Status.ToUpperInvariant() + ' ' + $Name + ': ' + $Detail)
}

function Show-LogTail([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Get-Content -LiteralPath $Path -Encoding OEM -Tail 12 | ForEach-Object { Write-Host $_ }
    }
}

function Invoke-CSharpCompiler([string[]]$CompilerArguments, [string]$Log) {
    # Framework csc writes the Windows OEM encoding when its output is redirected.
    # Preserve it during capture, then restore the host's output encoding.
    $previousEncoding = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage)
        & $compiler @CompilerArguments *> $Log
        return $LASTEXITCODE
    }
    finally { [Console]::OutputEncoding = $previousEncoding }
}

function Build-Runner([string]$Name, [string]$Main, [string]$TestFile) {
    $executable = Join-Path $binRoot ($Name + '.exe')
    $compileLog = Join-Path $logRoot ($Name + '-compile.log')
    $buildSources = @($sources)
    if ($TestFile) { $buildSources += $TestFile }
    $hashes = @($buildSources | ForEach-Object {
        [pscustomobject]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
    [IO.File]::WriteAllText((Join-Path $logRoot ($Name + '-sources.json')), (ConvertTo-Json -InputObject $hashes -Depth 3), $utf8)
    $arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001', ('/main:' + $Main),
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
        '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll',
        '/reference:Microsoft.CSharp.dll', '/reference:System.Web.Extensions.dll', ('/out:' + $executable)) + $buildSources
    $compilerExit = Invoke-CSharpCompiler $arguments $compileLog
    if ($compilerExit -ne 0) {
        Add-Result ($Name + 'Build') 'Fail' ('Compiler exit ' + $compilerExit + '; ' + $compileLog)
        Show-LogTail $compileLog
        return $null
    }
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'app.config') -Destination ($executable + '.config')
    Add-Result ($Name + 'Build') 'Pass' ('Compiled all ' + $sources.Count + ' production files' + $(if ($TestFile) { ' with the dedicated test Main.' } else { ' with the production Program Main.' }))
    return $executable
}

function Run-Runner([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds = 180) {
    $standardLog = Join-Path $logRoot ($Name + '.log')
    $errorLog = Join-Path $logRoot ($Name + '-errors.log')
    # These paths come from this script; quoting only preserves spaces in paths.
    $quotedArguments = @($Arguments | ForEach-Object { '"' + $_ + '"' })
    $process = Start-Process -FilePath $Executable -ArgumentList $quotedArguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $standardLog -RedirectStandardError $errorLog
    # Cache the native handle before waiting; Windows PowerShell otherwise can
    # lose access to ExitCode when a short-lived redirected process exits.
    $processHandle = $process.Handle
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $process.WaitForExit()
        Add-Result $Name 'Fail' ('Timed out after ' + $TimeoutSeconds + 's; ' + $standardLog)
        Show-LogTail $standardLog
        Show-LogTail $errorLog
        return
    }
    $process.WaitForExit()
    $process.Refresh()
    if ($null -eq $process.ExitCode) {
        Add-Result $Name 'Fail' ('Process exit code is unavailable; ' + $standardLog)
        Show-LogTail $standardLog
        Show-LogTail $errorLog
    }
    elseif ($process.ExitCode -ne 0) {
        Add-Result $Name 'Fail' ('Test exit ' + $process.ExitCode + '; ' + $standardLog)
        Show-LogTail $standardLog
        Show-LogTail $errorLog
    }
    else {
        $lastLine = @(Get-Content -LiteralPath $standardLog -Encoding OEM -Tail 1)
        Add-Result $Name 'Pass' ($lastLine -join '')
    }
}

function Build-ProcessFixture {
    $fixtureSource = Join-Path $PSScriptRoot 'DesktopProcessFixture.cs'
    if (-not (Test-Path -LiteralPath $fixtureSource -PathType Leaf)) {
        Add-Result 'ProcessFixture' 'NotImplemented' ('Required dedicated fixture is absent: ' + $fixtureSource)
        return $null
    }
    $fixtureExecutable = Join-Path $binRoot 'DesktopProcessFixture.exe'
    $compileLog = Join-Path $logRoot 'ProcessFixture-compile.log'
    $arguments = @('/nologo', '/target:winexe', '/platform:x64', '/codepage:65001', '/reference:System.dll',
        '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', ('/out:' + $fixtureExecutable), $fixtureSource)
    $compilerExit = Invoke-CSharpCompiler $arguments $compileLog
    if ($compilerExit -ne 0) {
        Add-Result 'ProcessFixtureBuild' 'Fail' ('Compiler exit ' + $compilerExit + '; ' + $compileLog)
        Show-LogTail $compileLog
        return $null
    }
    Add-Result 'ProcessFixtureBuild' 'Pass' 'Built the dedicated close/save-prompt fixture; no user application is a test target.'
    return $fixtureExecutable
}

function Read-FormalHashes {
    $formalRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnightAssistant'
    $names = @('adventure-v1.txt', 'cultivation-v2.txt', 'baiye-v1.txt', 'lingyu-v1.txt', 'dongtian-v1.txt', 'maintenance-consent.txt')
    $snapshot = @{}
    if (Test-Path -LiteralPath $formalRoot -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $formalRoot -File) {
            foreach ($name in $names) {
                if ($file.Name.StartsWith($name, [StringComparison]::OrdinalIgnoreCase)) {
                    $snapshot[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                    break
                }
            }
        }
    }
    return $snapshot
}

$formalBefore = Read-FormalHashes
[IO.File]::WriteAllText((Join-Path $logRoot 'formal-before.json'), (ConvertTo-Json -InputObject $formalBefore), $utf8)
Write-Host ('Ecology verification artifacts: ' + $runRoot)
try {
    # Always prove the unmodified production source set still builds independently.
    $baselineExecutable = Build-Runner 'Baseline' 'NetBunny.Program' $null
    $suiteNames = $(if ($Suite -eq 'All') { @('Policy', 'Sources', 'Maintenance', 'Processes', 'Cave', 'Journey', 'Equipment', 'Combat', 'Realm', 'AutoBattle', 'Guidance', 'SuspendCore', 'LingyuFlow', 'Spacing', 'ConsentFeedback', 'MaterialGuidance', 'DisplayLayout', 'MemoryOutcomes', 'IdleTargetBadge', 'FinalPathBuffer', 'CensusCadence', 'ReturnTasks', 'CaveSlots', 'PanelReopen') } else { @($Suite) })
    $suiteFiles = @{
        Policy = 'DesktopEnvironmentTests'
        Sources = 'DesktopSourceTests'
        Maintenance = 'DesktopMaintenanceTests'
        Processes = 'DesktopProcessTests'
        Cave = 'DongtianExpansionTests'
        Journey = 'DesktopJourneyTests'
        Equipment = 'DesktopEquipmentFlowTests'
        Combat = 'DesktopCombatFeedbackTests'
        Realm = 'LingyuRealmGateTests'
        AutoBattle = 'DesktopAutoBattleConsentTests'
        Guidance = 'BreakthroughGuidanceTests'
        SuspendCore = 'LingyuSuspendCoreTests'
        LingyuFlow = 'LingyuDesktopFlowTests'
        Spacing = 'DesktopEncounterSpacingTests'
        ConsentFeedback = 'DesktopConsentFeedbackTests'
        MaterialGuidance = 'DesktopMaterialGuidanceTests'
        DisplayLayout = 'DesktopDisplayLayoutTests'
        MemoryOutcomes = 'DesktopMemoryOutcomeTests'
        IdleTargetBadge = 'DesktopIdleTargetBadgeTests'
        FinalPathBuffer = 'DesktopFinalPathBufferTests'
        CensusCadence = 'DesktopCensusCadenceTests'
        ReturnTasks = 'DesktopReturnTasksTests'
        CaveSlots = 'DesktopCaveSlotsTests'
        PanelReopen = 'DesktopPanelReopenTests'
    }
    foreach ($suiteName in $suiteNames) {
        $className = $suiteFiles[$suiteName]
        $testFile = Join-Path $PSScriptRoot ($className + '.cs')
        if (-not (Test-Path -LiteralPath $testFile -PathType Leaf)) {
            Add-Result $suiteName 'NotImplemented' ('Required suite is absent: ' + $testFile)
            continue
        }
        $executable = Build-Runner $className ('NetBunny.' + $className) $testFile
        if ($executable) {
            $evidence = Join-Path $binRoot ('evidence-' + $suiteName.ToLowerInvariant())
            [IO.Directory]::CreateDirectory($evidence) | Out-Null
            $firstArgument = $assetRoot
            if ($suiteName -eq 'Processes') { $firstArgument = Build-ProcessFixture }
            if ($suiteName -eq 'Maintenance') {
                $ownedProcessFixture = Build-ProcessFixture
                if ($ownedProcessFixture) { Run-Runner $suiteName $executable @($firstArgument, $evidence, $ownedProcessFixture) }
            } elseif ($suiteName -in @('SuspendCore', 'ConsentFeedback', 'MemoryOutcomes', 'FinalPathBuffer')) { Run-Runner $suiteName $executable @($evidence) }
            elseif ($suiteName -in @('Equipment', 'LingyuFlow', 'MaterialGuidance', 'DisplayLayout', 'IdleTargetBadge', 'CensusCadence', 'ReturnTasks', 'CaveSlots', 'PanelReopen')) { Run-Runner $suiteName $executable @($firstArgument, $evidence, (Join-Path $sourceRoot 'ArtDemoAssets')) }
            elseif ($firstArgument) { Run-Runner $suiteName $executable @($firstArgument, $evidence) }
        }
    }
    if ($Suite -eq 'Policy' -or $Suite -eq 'All') {
        foreach ($className in @('DesktopDemoStateTests', 'DesktopPlayerViewTests')) {
            $testFile = Join-Path $PSScriptRoot ($className + '.cs')
            if (-not (Test-Path -LiteralPath $testFile -PathType Leaf)) {
                Add-Result $className 'NotImplemented' ('Required regression is absent: ' + $testFile)
                continue
            }
            $executable = Build-Runner $className ('NetBunny.' + $className) $testFile
            if ($executable) {
                $evidence = Join-Path $binRoot ('evidence-' + $className)
                [IO.Directory]::CreateDirectory($evidence) | Out-Null
                Run-Runner $className $executable @($assetRoot, $evidence)
            }
        }
    }
    if ($Baseline -and $baselineExecutable) {
        Copy-Item -LiteralPath $assetRoot -Destination (Join-Path $binRoot 'Assets') -Recurse
        Run-Runner 'SelfTest' $baselineExecutable @('--selftest')
        Run-Runner 'AdventureSmoke' $baselineExecutable @('--adventure-smoke')
    }
}
catch {
    Add-Result 'Harness' 'Fail' $_.Exception.Message
}
finally {
    try {
        $formalAfter = Read-FormalHashes
        [IO.File]::WriteAllText((Join-Path $logRoot 'formal-after.json'), (ConvertTo-Json -InputObject $formalAfter), $utf8)
        $unchanged = $formalBefore.Count -eq $formalAfter.Count
        foreach ($path in $formalBefore.Keys) {
            if (-not $formalAfter.ContainsKey($path) -or $formalBefore[$path] -ne $formalAfter[$path]) { $unchanged = $false }
        }
        if ($unchanged) { Add-Result 'FormalArchiveIsolation' 'Pass' ($formalBefore.Count.ToString() + ' archive/authorization SHA256 hashes unchanged.') }
        else { Add-Result 'FormalArchiveIsolation' 'Fail' 'Formal archive or authorization state changed; inspect formal-before.json and formal-after.json.' }
    }
    catch { Add-Result 'FormalArchiveIsolation' 'Fail' $_.Exception.Message }
    [IO.File]::WriteAllText((Join-Path $runRoot 'summary.json'), (ConvertTo-Json -InputObject @($results.ToArray()) -Depth 4), $utf8)
}
$failed = @($results | Where-Object Status -ne 'Pass')
Write-Host ('Ecology verification: ' + $results.Count + ' results, ' + $failed.Count + ' non-passing; logs: ' + $logRoot)
if ($failed.Count -gt 0) { exit 1 }
exit 0
