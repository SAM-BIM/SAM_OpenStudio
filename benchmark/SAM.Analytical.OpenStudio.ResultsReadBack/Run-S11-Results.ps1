# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

<#
.SYNOPSIS
  S11 OpenStudio results round-trip acceptance (revision 2 of the S11 plan; NOT authorised by revision 1).

.DESCRIPTION
  -Phase run (2 EnergyPlus simulations; refuses without -ConfirmEngineRuns): preflight (fixture hashes against
   InputReadBack\Fixtures\SHA256SUMS.txt, Budapest EPW hash, -Root new or empty, the build receipt of
   Build-S11-Receipt.ps1 against the files on disk and the clean HEADs, the portable OpenStudio CLI 3.10.0), then for
   s08-fix-box and s08-fix-two: benchmark-openstudio with --sam-results, the CLI forced by prepending its bin to the
   child PATH only. Writes run-manifest.json per run.
  -Phase readback: read, subject and compare for both runs, each a separate process.
  -Phase negative: the os-results-readback negative cases on copies of both runs, each read/subject/compared fresh.

  Before and after each run is processed, its model, in.idf, SQL, ESO, b1a.json and sam.json must still have the
  run-manifest hashes, and the active build receipt (and the harness and producer it lists) must be the one the run
  recorded: evidence of different builds is never combined.

  Exit codes: 70 STOPPED (infrastructure or integrity: a step that did not complete, an output it did not write fresh,
  a report whose verdict disagrees with its exit or that judged other files, a changed original); the phase stops at
  once and no earlier report is read in its place. Otherwise readback gives 0 PASS (every required row of both
  fixtures PASS and OD-7 met by at least one), 10 FAIL (a FAIL in either), 11 BLOCKED; numerical FAIL/BLOCKED outcomes
  are collected for both fixtures first. negative gives 0 when every case gave its required exit on a copy mutated as
  designed and each control PASSed, else 10; without a PASSing control no mutation outcome counts as evidence.
  summary-<phase>.json under -Root records each attempt, including a stopped one. SQL/ESO stay machine-local.

  -BenchmarkExe and -HarnessExe exist for the offline driver tests (stand-in commands); whatever is used must be a file
  of the build receipt.
#>
param(
    [ValidateSet('run', 'readback', 'negative')]
    [string]$Phase = 'readback',
    [string]$Root = 'C:\TasOut\s11\acc1',
    [string]$WeatherEpw = '',
    [string]$WeatherEpwSha256 = '472ffb4e2f10867600949a392ce46891fc1bd05f2fc8d8917a99bca7d26209c3',
    [string]$OpenStudioCli = 'C:\Users\michal\Documents\GitHub\SAM-BIM\.icon-migration\runtime\OpenStudio-3.10.0+86d7e215a1-Windows\bin\openstudio.exe',
    [string]$OpenStudioCliVersion = '3.10.0+86d7e215a1',
    [string]$BuildReceipt = '',
    [string]$BenchmarkExe = '',
    [string]$HarnessExe = '',
    [switch]$ConfirmEngineRuns
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$workspace = Split-Path $repository
$fixtures = Join-Path $repository 'benchmark\SAM.Analytical.OpenStudio.InputReadBack\Fixtures'
if (-not $BenchmarkExe) { $BenchmarkExe = Join-Path $repository 'benchmark\SAM.Analytical.OpenStudio.Benchmark\bin\x64\Release\net8.0\benchmark-openstudio.exe' }
if (-not $HarnessExe) { $HarnessExe = Join-Path $repository 'benchmark\SAM.Analytical.OpenStudio.ResultsReadBack.Cli\bin\x64\Release\net8.0\os-results-readback.exe' }
$BenchmarkExe = [IO.Path]::GetFullPath($BenchmarkExe)
$HarnessExe = [IO.Path]::GetFullPath($HarnessExe)
$benchmarkDll = [IO.Path]::ChangeExtension($BenchmarkExe, '.dll')
$harnessDll = [IO.Path]::ChangeExtension($HarnessExe, '.dll')
if (-not $BuildReceipt) { $BuildReceipt = Join-Path (Split-Path $HarnessExe) 's11-build-receipt.json' }
$BuildReceipt = [IO.Path]::GetFullPath($BuildReceipt)
$runs = @(@('s08-fix-box', 'box'), @('s08-fix-two', 'two'))

# The exit each negative case must give and the copied files it must change. Owned by this driver: a case's own
# negative.json is checked against it, never trusted for it. The control comes first.
$negatives = [ordered]@{
    'ctl-unmutated-copy' = 0
    'neg-delete-hour-both' = 11
    'neg-duplicate-hour-both' = 11
    'neg-sensible-cooling-substitution' = 10
}
$mutates = @{
    'ctl-unmutated-copy' = @()
    'neg-delete-hour-both' = @('eplusout.sql', 'eplusout.eso')
    'neg-duplicate-hour-both' = @('eplusout.sql', 'eplusout.eso')
    'neg-sensible-cooling-substitution' = @('b1a.json', 'sam.json')
}
$verdicts = @{ 0 = 'PASS'; 10 = 'FAIL'; 11 = 'BLOCKED' }

$summary = [ordered]@{ phase = $Phase; started = (Get-Date).ToUniversalTime().ToString('o'); root = $Root; buildReceipt = $BuildReceipt; harness = $HarnessExe; runs = @() }
$summaryWritable = $false

function Write-Summary([int]$Code, [string]$Result) {
    $summary.exit = $Code
    $summary.result = $Result
    $summary.finished = (Get-Date).ToUniversalTime().ToString('o')
    if ($script:summaryWritable) { $summary | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $Root "summary-$Phase.json") }
}

function Stop-Phase([string]$Reason) {
    Write-Host "STOPPED: $Reason"
    $summary.stopped = $Reason
    Write-Summary 70 'STOPPED'
    exit 70
}

function Sha([string]$Path) {
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Git([string]$Directory) {
    $ErrorActionPreference = 'Continue'
    & git -C $Directory @args 2>$null
}

# A step's exit code; a step that cannot start or reports none stops the phase.
function Invoke-Step([string]$Exe, [string[]]$Arguments, [string]$Log) {
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = $null
    try { & $Exe @Arguments *>> $Log }
    catch { Stop-Phase "$Exe $($Arguments[0]) could not run: $($_.Exception.Message)" }
    if ($null -eq $global:LASTEXITCODE) { Stop-Phase "$Exe $($Arguments[0]) reported no exit code (see $Log)" }
    return [int]$global:LASTEXITCODE
}

# A file as a step finds it (write time and hash); null when absent. Taken before and after a step, so freshness never
# depends on comparing clocks.
function Get-Stamp([string]$Path) {
    $item = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $item) { return $null }
    return "$($item.LastWriteTimeUtc.Ticks)|$(Sha $Path)"
}

# The output a step that exited 0 must have written itself: present, and not the file an earlier attempt left.
function Assert-Fresh([string]$Path, $Before, [string]$What) {
    $after = Get-Stamp $Path
    if (-not $after) { Stop-Phase "$What wrote no $Path" }
    if ($after -eq $Before) { Stop-Phase "$What did not write $Path (it is the file from before the step)" }
}

# The receipt's files must be the files on disk, its commits the clean HEADs now, and the executables this driver
# starts must be among its files.
function Test-Receipt {
    if (-not (Test-Path -LiteralPath $BuildReceipt -PathType Leaf)) { Stop-Phase "no build receipt $BuildReceipt (run Build-S11-Receipt.ps1)" }
    $receipt = Get-Content -LiteralPath $BuildReceipt -Raw | ConvertFrom-Json
    if ($receipt.schema -ne 's11-build-receipt/1') { Stop-Phase "receipt schema $($receipt.schema)" }
    $listed = @{}
    foreach ($file in @($receipt.files)) {
        if (-not $file.path -or -not $file.sha256 -or (Sha $file.path) -ne $file.sha256) { Stop-Phase "$($file.path) is not the file the receipt recorded" }
        $listed[[IO.Path]::GetFullPath($file.path)] = $true
    }
    foreach ($file in @($BenchmarkExe, $benchmarkDll, $HarnessExe, $harnessDll)) {
        if (-not $listed.ContainsKey($file)) { Stop-Phase "$file is not a file of the build receipt $BuildReceipt" }
    }
    foreach ($commit in @($receipt.commits)) {
        $head = (Git $commit.repository rev-parse HEAD | Out-String).Trim()
        $status = @(Git $commit.repository status --porcelain --untracked-files=all)
        if ($head -ne $commit.commit -or $status.Count -gt 0) { Stop-Phase "$($commit.repository) is not clean at the receipt commit $($commit.commit)" }
    }
}

# Every file a run's evidence rests on, with the hash its run-manifest recorded.
function Get-RunFiles($Manifest, [string]$Directory, [string]$FixtureId) {
    return [ordered]@{
        'model' = @((Join-Path $fixtures "$FixtureId.json"), $Manifest.fixtureSha256)
        'in.idf' = @([string]$Manifest.idf, $Manifest.idfSha256)
        'eplusout.sql' = @([string]$Manifest.sql, $Manifest.sqlSha256)
        'eplusout.eso' = @([string]$Manifest.eso, $Manifest.esoSha256)
        'b1a.json' = @((Join-Path $Directory 'b1a.json'), $Manifest.b1aSha256)
        'sam.json' = @((Join-Path $Directory 'sam.json'), $Manifest.samResultsSha256)
        'build receipt' = @($BuildReceipt, $Manifest.receiptSha256)
        'harness' = @($harnessDll, $Manifest.harnessDll)
        'producer' = @($benchmarkDll, $Manifest.benchmarkDll)
    }
}

# Stops unless the run's files are still those of its manifest and the active build is the one it recorded.
function Test-Run([string]$Label, [string]$FixtureId, [string]$Directory, [string]$When) {
    $path = Join-Path $Directory 'run-manifest.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-Phase "$Label has no run-manifest.json ($When)" }
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 's11-run-manifest/1' -or $manifest.label -ne $Label -or $manifest.fixture -ne $FixtureId) { Stop-Phase "$path is not the s11-run-manifest/1 of $Label ($FixtureId)" }
    $below = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $problems = @()
    foreach ($name in @('idf', 'sql', 'eso')) {
        $file = [string]$manifest.$name
        if (-not $file -or -not [IO.Path]::GetFullPath($file).StartsWith($below, [StringComparison]::OrdinalIgnoreCase)) { $problems += "the manifest $name '$file' is not below $Directory" }
    }
    $files = Get-RunFiles $manifest $Directory $FixtureId
    foreach ($name in $files.Keys) {
        $recorded = $files[$name][1]
        if (-not $recorded -or (Sha $files[$name][0]) -ne $recorded) { $problems += "$name is not the file the run recorded" }
    }
    if ($problems.Count -gt 0) {
        $summary["original-unchanged:$Label"] = $false
        Stop-Phase "$Label ${When}: $($problems -join '; ')"
    }
    return $manifest
}

# A compare that completed (0/10/11) must have written a fresh report whose verdict is its exit and whose inputs are
# exactly the files given; any other exit stops the phase before a report is read.
function Read-Compare([string]$Directory, [int]$Exit, $Before, $Inputs, [string]$What) {
    if (-not $verdicts.ContainsKey($Exit)) { Stop-Phase "$What exit $Exit (infrastructure or usage; no report is read)" }
    $path = Join-Path $Directory 'compare\compare.json'
    Assert-Fresh $path $Before $What
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report.Schema -ne 's11-compare/1') { Stop-Phase "$path schema $($report.Schema)" }
    if ($report.Verdict -ne $verdicts[$Exit] -or [int]$report.ExitCode -ne $Exit) { Stop-Phase "$What exit $Exit but its report says $($report.Verdict) (exit $($report.ExitCode))" }
    foreach ($name in $Inputs.Keys) {
        if (-not $Inputs[$name] -or $report.Inputs.$name -ne $Inputs[$name]) { Stop-Phase "$What judged a $name other than $($Inputs[$name])" }
    }
    return $report
}

# read, then subject (unless the case captured it), each in its own process and each writing its output fresh.
function Invoke-ReadSubject([string]$Directory, [string]$Model, [string]$FixtureId, [string]$Log, [string]$What, [bool]$Subject) {
    $steps = @(, @('read', 'reference.json', @('--fixture-id', $FixtureId)))
    if ($Subject) { $steps += , @('subject', 'subject.json', @()) }
    foreach ($step in $steps) {
        $before = Get-Stamp (Join-Path $Directory $step[1])
        $exit = Invoke-Step $HarnessExe (@($step[0], '--run', $Directory, '--model', $Model) + $step[2]) $Log
        if ($exit -ne 0) { Stop-Phase "$($step[0]) $What exit $exit (see $Log)" }
        Assert-Fresh (Join-Path $Directory $step[1]) $before "$($step[0]) $What"
    }
}

if ($Phase -eq 'run') {
    if (-not $ConfirmEngineRuns) { Stop-Phase 'the run phase starts 2 EnergyPlus simulations: pass -ConfirmEngineRuns only under an explicit owner authorisation' }
    if (Test-Path -LiteralPath $Root) {
        if (@(Get-ChildItem -LiteralPath $Root -Force).Count -gt 0) { Stop-Phase "Root must be new or empty: $Root" }
    }
    else { New-Item -ItemType Directory -Path $Root | Out-Null }
    $summaryWritable = $true

    Test-Receipt
    foreach ($line in Get-Content (Join-Path $fixtures 'SHA256SUMS.txt')) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $hash, $name = $line -split '\s+', 2
        if ((Sha (Join-Path $fixtures $name.Trim())) -ne $hash.ToLowerInvariant()) { Stop-Phase "fixture $name is not the signed-off $hash" }
    }
    if (-not $WeatherEpw -or (Sha $WeatherEpw) -ne $WeatherEpwSha256) { Stop-Phase "the EPW $WeatherEpw is not $WeatherEpwSha256" }
    if (-not (Test-Path -LiteralPath $OpenStudioCli)) { Stop-Phase "OpenStudio CLI not found: $OpenStudioCli" }
    $cliVersion = (& $OpenStudioCli --version | Out-String).Trim()
    if ($cliVersion -ne $OpenStudioCliVersion) { Stop-Phase "OpenStudio CLI version '$cliVersion' is not $OpenStudioCliVersion" }

    $runnerHead = (Git $repository rev-parse HEAD | Out-String).Trim()
    $samHead = (Git (Join-Path $workspace 'SAM') rev-parse HEAD | Out-String).Trim()
    foreach ($run in $runs) {
        $fixtureId, $label = $run
        $dir = Join-Path $Root $label
        New-Item -ItemType Directory -Path $dir | Out-Null
        $log = Join-Path $Root "$label.console.txt"
        $model = Join-Path $fixtures "$fixtureId.json"
        $path = $env:PATH
        try {
            # The child resolves openstudio.exe from PATH first, and that CLI matches the SDK, so it is selected.
            $env:PATH = (Split-Path $OpenStudioCli) + ';' + $path
            $exit = Invoke-Step $BenchmarkExe @('--model', $model, '--weather', $WeatherEpw, '--out', (Join-Path $dir 'b1a.json'), '--work', (Join-Path $dir 'work'), '--sam-results', (Join-Path $dir 'sam.json'), '--runner-commit', $runnerHead, '--sam-commit', $samHead) $log
        }
        finally { $env:PATH = $path }
        if ($exit -ne 0) { Stop-Phase "benchmark-openstudio $label exit $exit (no retry; see $log)" }
        $sql = @(Get-ChildItem -LiteralPath (Join-Path $dir 'work') -Recurse -Filter 'eplusout.sql')
        $eso = @(Get-ChildItem -LiteralPath (Join-Path $dir 'work') -Recurse -Filter 'eplusout.eso')
        if ($sql.Count -ne 1 -or $eso.Count -ne 1) { Stop-Phase "$label has $($sql.Count) eplusout.sql and $($eso.Count) eplusout.eso (exactly one each)" }
        $idf = Join-Path $sql[0].DirectoryName 'in.idf'
        $manifest = [ordered]@{
            schema = 's11-run-manifest/1'; label = $label; fixture = $fixtureId; fixtureSha256 = (Sha $model); epwSha256 = (Sha $WeatherEpw)
            openStudioCli = $OpenStudioCli; openStudioCliVersion = $cliVersion; runnerCommit = $runnerHead; samCommit = $samHead
            buildReceipt = $BuildReceipt; receiptSha256 = (Sha $BuildReceipt); benchmarkExe = $BenchmarkExe; benchmarkDll = (Sha $benchmarkDll); harnessExe = $HarnessExe; harnessDll = (Sha $harnessDll)
            idf = $idf; idfSha256 = (Sha $idf); sql = $sql[0].FullName; sqlSha256 = (Sha $sql[0].FullName); eso = $eso[0].FullName; esoSha256 = (Sha $eso[0].FullName)
            b1aSha256 = (Sha (Join-Path $dir 'b1a.json')); samResultsSha256 = (Sha (Join-Path $dir 'sam.json'))
        }
        foreach ($name in @('idfSha256', 'sqlSha256', 'esoSha256', 'b1aSha256', 'samResultsSha256')) {
            if (-not $manifest[$name]) { Stop-Phase "$label produced no file for $name" }
        }
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $dir 'run-manifest.json')
        $summary.runs += $manifest
        Write-Host "$label simulated: SQL $($manifest.sqlSha256)"
    }
    Write-Summary 0 'SIMULATED'
    exit 0
}

if (-not (Test-Path -LiteralPath $Root -PathType Container)) { Stop-Phase "Root does not exist: $Root" }
$summaryWritable = $true
Test-Receipt

if ($Phase -eq 'readback') {
    $exits = @()
    foreach ($run in $runs) {
        $fixtureId, $label = $run
        $dir = Join-Path $Root $label
        $log = Join-Path $Root "$label.readback.txt"
        $model = Join-Path $fixtures "$fixtureId.json"
        $manifest = Test-Run $label $fixtureId $dir 'before readback'
        Invoke-ReadSubject $dir $model $fixtureId $log $label $true
        $before = Get-Stamp (Join-Path $dir 'compare\compare.json')
        $compare = Invoke-Step $HarnessExe @('compare', '--run', $dir, '--model', $model) $log
        Test-Run $label $fixtureId $dir 'after readback' | Out-Null
        $inputs = [ordered]@{}
        foreach ($entry in (Get-RunFiles $manifest $dir $fixtureId).GetEnumerator()) {
            if (@('model', 'in.idf', 'eplusout.sql', 'eplusout.eso', 'b1a.json', 'sam.json') -contains $entry.Key) { $inputs[$entry.Key] = $entry.Value[1] }
        }
        $inputs['reference.json'] = Sha (Join-Path $dir 'reference.json')
        $inputs['subject.json'] = Sha (Join-Path $dir 'subject.json')
        $report = Read-Compare $dir $compare $before $inputs "compare $label"
        $summary["original-unchanged:$label"] = $true
        $exits += $compare
        $summary.runs += [ordered]@{ label = $label; fixture = $fixtureId; exit = $compare; verdict = $report.Verdict; required = $report.Required; passed = $report.Passed; failed = $report.Failed; blocked = $report.Blocked; f3 = $report.F3; compareSha256 = (Sha (Join-Path $dir 'compare\compare.json')) }
        Write-Host ("{0,-6} compare exit {1} ({2}: {3}/{4} PASS, {5} FAIL, {6} BLOCKED)" -f $label, $compare, $report.Verdict, $report.Passed, $report.Required, $report.Failed, $report.Blocked)
    }
    $od7 = @($summary.runs | Where-Object { $_.f3.MeetsOd7 -eq $true }).Count -gt 0
    $summary.od7 = [ordered]@{ anyFixtureMeets = $od7; note = 'OD-7: on at least one fixture, cooling consumption and one space cooling peak discriminate Total from Sensible by >= 1000 x tolerance (required for PASS)' }
    if ($exits -contains 10) { $code = 10 }
    elseif ($exits -contains 11 -or -not $od7) { $code = 11 }
    else { $code = 0 }
    Write-Host ("readback: {0}{1}" -f $verdicts[$code], $(if ($code -eq 11 -and -not ($exits -contains 11)) { ' (OD-7 not met by either fixture)' } else { '' }))
    Write-Summary $code $verdicts[$code]
    exit $code
}

# negative
$failures = @()
foreach ($run in $runs) {
    $fixtureId, $label = $run
    $source = Join-Path $Root $label
    $model = Join-Path $fixtures "$fixtureId.json"
    $manifest = Test-Run $label $fixtureId $source 'before negatives'
    $originals = Get-RunFiles $manifest $source $fixtureId
    $controlPassed = $false
    foreach ($caseId in $negatives.Keys) {
        $required = $negatives[$caseId]
        $dir = Join-Path $Root "negatives\$label-$caseId"
        $log = Join-Path $Root "negatives\$label-$caseId.console.txt"
        $what = "$label-$caseId"
        New-Item -ItemType Directory -Force -Path (Split-Path $dir) | Out-Null
        $beforeRecord = Get-Stamp (Join-Path $dir 'negative.json')
        $beforeSubject = Get-Stamp (Join-Path $dir 'subject.json')
        $exit = Invoke-Step $HarnessExe @('negative', '--run', $source, '--model', $model, '--case', $caseId, '--out', $dir) $log
        if ($exit -ne 0) { Stop-Phase "negative $what exit $exit (see $log)" }
        Assert-Fresh (Join-Path $dir 'negative.json') $beforeRecord "negative $what"
        $record = Get-Content -LiteralPath (Join-Path $dir 'negative.json') -Raw | ConvertFrom-Json
        if ($record.Case -ne $caseId -or [int]$record.ExpectedExit -ne $required) { Stop-Phase "negative.json of $what describes case '$($record.Case)' with exit $($record.ExpectedExit), not '$caseId' with exit $required" }

        # The copy must differ from the original in exactly the files the case mutates.
        $copy = [ordered]@{
            'in.idf' = Sha (Join-Path $dir 'work\run\in.idf'); 'eplusout.sql' = Sha (Join-Path $dir 'work\run\eplusout.sql'); 'eplusout.eso' = Sha (Join-Path $dir 'work\run\eplusout.eso')
            'b1a.json' = Sha (Join-Path $dir 'b1a.json'); 'sam.json' = Sha (Join-Path $dir 'sam.json')
        }
        $design = @()
        foreach ($name in $copy.Keys) {
            if (-not $copy[$name]) { Stop-Phase "negative $what copied no $name" }
            $changed = $copy[$name] -ne $originals[$name][1]
            if ($changed -ne ($mutates[$caseId] -contains $name)) { $design += "$name $(if ($changed) { 'changed' } else { 'unchanged' })" }
        }

        $captured = $caseId -eq 'neg-sensible-cooling-substitution'
        if ($captured) { Assert-Fresh (Join-Path $dir 'subject.json') $beforeSubject "negative $what (captured subject)" }
        Invoke-ReadSubject $dir $model $fixtureId $log $what (-not $captured)
        $before = Get-Stamp (Join-Path $dir 'compare\compare.json')
        $exit = Invoke-Step $HarnessExe @('compare', '--run', $dir, '--model', $model) $log

        # The original must be untouched by every case.
        Test-Run $label $fixtureId $source "after $caseId" | Out-Null
        $inputs = [ordered]@{ 'model' = $originals['model'][1] }
        foreach ($name in $copy.Keys) { $inputs[$name] = $copy[$name] }
        $inputs['reference.json'] = Sha (Join-Path $dir 'reference.json')
        $inputs['subject.json'] = Sha (Join-Path $dir 'subject.json')
        $report = Read-Compare $dir $exit $before $inputs "compare $what"

        $asRequired = ($exit -eq $required) -and ($design.Count -eq 0)
        if ($caseId -eq 'ctl-unmutated-copy') { $controlPassed = $asRequired }
        $evidence = $controlPassed
        if (-not $asRequired) { $failures += "$what exit $exit (required $required)$(if ($design.Count -gt 0) { '; copy not mutated as designed: ' + ($design -join ', ') })" }
        $summary.runs += [ordered]@{ label = $what; exit = $exit; required = $required; asRequired = $asRequired; evidence = $evidence; copyAsDesigned = ($design.Count -eq 0); copyProblems = $design; verdict = $report.Verdict; mutation = $record.Mutation; f3 = $report.F3 }
        Write-Host ("{0,-44} exit {1} (required {2}) {3}" -f $what, $exit, $required, $(if ($asRequired) { 'as required' } else { 'NOT AS REQUIRED' }))
    }
    $summary["original-unchanged:$label"] = $true
    if (-not $controlPassed) { $failures += "$label control did not PASS: no mutation outcome of $label is evidence" }
}

$summary.failures = $failures
$code = $(if ($failures.Count -gt 0) { 10 } else { 0 })
Write-Host ("negative: {0}" -f $(if ($code -eq 0) { 'every case as required' } else { "$($failures.Count) failure(s): " + ($failures -join '; ') }))
Write-Summary $code $(if ($code -eq 0) { 'AS REQUIRED' } else { 'NOT AS REQUIRED' })
exit $code
