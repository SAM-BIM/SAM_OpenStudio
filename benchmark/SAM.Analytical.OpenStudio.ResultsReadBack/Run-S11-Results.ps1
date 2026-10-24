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
   child PATH only. Any infrastructure failure stops the phase; nothing is retried.
  -Phase readback: read, subject and compare for both runs, each a separate process; the SQL/ESO hashes must equal the
   run manifest before and after.
  -Phase negative: the os-results-readback negative cases on copies of both runs, each read/subject/compared fresh; each
   must give its required exit, and the originals must be unchanged.
  Writes run-manifest.json per run and summary-<phase>.json under -Root. SQL/ESO stay machine-local under -Root.
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
    [switch]$ConfirmEngineRuns
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$workspace = Split-Path $repository
$fixtures = Join-Path $repository 'benchmark\SAM.Analytical.OpenStudio.InputReadBack\Fixtures'
$benchmarkExe = Join-Path $repository 'benchmark\SAM.Analytical.OpenStudio.Benchmark\bin\x64\Release\net8.0\benchmark-openstudio.exe'
$harnessExe = Join-Path $repository 'benchmark\SAM.Analytical.OpenStudio.ResultsReadBack.Cli\bin\x64\Release\net8.0\os-results-readback.exe'
if (-not $BuildReceipt) { $BuildReceipt = Join-Path (Split-Path $harnessExe) 's11-build-receipt.json' }
$runs = @(@('s08-fix-box', 'box'), @('s08-fix-two', 'two'))
$negatives = @('ctl-unmutated-copy', 'neg-delete-hour-both', 'neg-duplicate-hour-both', 'neg-sensible-cooling-substitution')

function Stop-Phase([string]$Reason) {
    Write-Host "STOPPED: $Reason"
    exit 70
}

function Sha([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
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

function Invoke-Step([string]$Exe, [string[]]$Arguments, [string]$Log) {
    & $Exe @Arguments *>> $Log
    return $LASTEXITCODE
}

# The receipt's files must be the files on disk, and its commits the clean HEADs now.
function Test-Receipt {
    if (-not (Test-Path -LiteralPath $BuildReceipt)) { Stop-Phase "no build receipt $BuildReceipt (run Build-S11-Receipt.ps1)" }
    $receipt = Get-Content -LiteralPath $BuildReceipt -Raw | ConvertFrom-Json
    if ($receipt.schema -ne 's11-build-receipt/1') { Stop-Phase "receipt schema $($receipt.schema)" }
    foreach ($file in $receipt.files) {
        if ((Sha $file.path) -ne $file.sha256) { Stop-Phase "$($file.path) is not the file the receipt recorded" }
    }
    foreach ($commit in $receipt.commits) {
        $head = (Git $commit.repository rev-parse HEAD | Out-String).Trim()
        $status = @(Git $commit.repository status --porcelain --untracked-files=all)
        if ($head -ne $commit.commit -or $status.Count -gt 0) { Stop-Phase "$($commit.repository) is not clean at the receipt commit $($commit.commit)" }
    }
    return $receipt
}

$summary = [ordered]@{ phase = $Phase; started = (Get-Date).ToUniversalTime().ToString('o'); runs = @() }

if ($Phase -eq 'run') {
    if (-not $ConfirmEngineRuns) { Stop-Phase 'the run phase starts 2 EnergyPlus simulations: pass -ConfirmEngineRuns only under an explicit owner authorisation' }
    if (Test-Path -LiteralPath $Root) {
        if (@(Get-ChildItem -LiteralPath $Root -Force).Count -gt 0) { Stop-Phase "Root must be new or empty: $Root" }
    }
    else { New-Item -ItemType Directory -Path $Root | Out-Null }

    $receipt = Test-Receipt
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
            $exit = Invoke-Step $benchmarkExe @('--model', $model, '--weather', $WeatherEpw, '--out', (Join-Path $dir 'b1a.json'), '--work', (Join-Path $dir 'work'), '--sam-results', (Join-Path $dir 'sam.json'), '--runner-commit', $runnerHead, '--sam-commit', $samHead) $log
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
            receiptSha256 = (Sha $BuildReceipt); benchmarkDll = (Sha ([IO.Path]::ChangeExtension($benchmarkExe, '.dll'))); harnessDll = (Sha ([IO.Path]::ChangeExtension($harnessExe, '.dll')))
            idf = $idf; idfSha256 = (Sha $idf); sql = $sql[0].FullName; sqlSha256 = (Sha $sql[0].FullName); eso = $eso[0].FullName; esoSha256 = (Sha $eso[0].FullName)
            b1aSha256 = (Sha (Join-Path $dir 'b1a.json')); samResultsSha256 = (Sha (Join-Path $dir 'sam.json'))
        }
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $dir 'run-manifest.json')
        $summary.runs += $manifest
        Write-Host "$label simulated: SQL $($manifest.sqlSha256)"
    }
}
elseif ($Phase -eq 'readback') {
    $receipt = Test-Receipt
    foreach ($run in $runs) {
        $fixtureId, $label = $run
        $dir = Join-Path $Root $label
        $log = Join-Path $Root "$label.readback.txt"
        $manifest = Get-Content -LiteralPath (Join-Path $dir 'run-manifest.json') -Raw | ConvertFrom-Json
        $model = Join-Path $fixtures "$fixtureId.json"
        foreach ($step in @(@('read', '--fixture-id', $fixtureId), @('subject'))) {
            $exit = Invoke-Step $harnessExe (@($step[0], '--run', $dir, '--model', $model) + @($step | Select-Object -Skip 1)) $log
            if ($exit -ne 0) { Stop-Phase "$($step[0]) $label exit $exit" }
        }
        $compare = Invoke-Step $harnessExe @('compare', '--run', $dir, '--model', $model) $log
        if ((Sha $manifest.sql) -ne $manifest.sqlSha256 -or (Sha $manifest.eso) -ne $manifest.esoSha256) { Stop-Phase "$label SQL/ESO changed since the run" }
        $report = Get-Content -LiteralPath (Join-Path $dir 'compare\compare.json') -Raw | ConvertFrom-Json
        $summary.runs += [ordered]@{ label = $label; fixture = $fixtureId; exit = $compare; verdict = $report.Verdict; required = $report.Required; passed = $report.Passed; failed = $report.Failed; blocked = $report.Blocked; f3 = $report.F3 }
        Write-Host ("{0,-6} compare exit {1} ({2}: {3}/{4} PASS, {5} FAIL, {6} BLOCKED)" -f $label, $compare, $report.Verdict, $report.Passed, $report.Required, $report.Failed, $report.Blocked)
    }
    $summary.od7 = [ordered]@{ anyFixtureMeets = (@($summary.runs | Where-Object { $_.f3.MeetsOd7 }).Count -gt 0); note = 'OD-7: on at least one fixture, cooling consumption and one space cooling peak discriminate Total from Sensible by >= 1000 x tolerance' }
}
else {
    $receipt = Test-Receipt
    foreach ($run in $runs) {
        $fixtureId, $label = $run
        $source = Join-Path $Root $label
        $model = Join-Path $fixtures "$fixtureId.json"
        $manifest = Get-Content -LiteralPath (Join-Path $source 'run-manifest.json') -Raw | ConvertFrom-Json
        foreach ($caseId in $negatives) {
            $dir = Join-Path $Root "negatives\$label-$caseId"
            $log = Join-Path $Root "negatives\$label-$caseId.console.txt"
            New-Item -ItemType Directory -Force -Path (Split-Path $dir) | Out-Null
            if ((Invoke-Step $harnessExe @('negative', '--run', $source, '--model', $model, '--case', $caseId, '--out', $dir) $log) -ne 0) { Stop-Phase "negative $label $caseId" }
            if ((Invoke-Step $harnessExe @('read', '--run', $dir, '--model', $model, '--fixture-id', $fixtureId) $log) -ne 0) { Stop-Phase "read $label $caseId" }
            if (-not (Test-Path -LiteralPath (Join-Path $dir 'subject.json'))) {
                if ((Invoke-Step $harnessExe @('subject', '--run', $dir, '--model', $model) $log) -ne 0) { Stop-Phase "subject $label $caseId" }
            }
            $exit = Invoke-Step $harnessExe @('compare', '--run', $dir, '--model', $model) $log
            $record = Get-Content -LiteralPath (Join-Path $dir 'negative.json') -Raw | ConvertFrom-Json
            $report = Get-Content -LiteralPath (Join-Path $dir 'compare\compare.json') -Raw | ConvertFrom-Json
            $summary.runs += [ordered]@{ label = "$label-$caseId"; exit = $exit; required = $record.ExpectedExit; asRequired = ($exit -eq $record.ExpectedExit); verdict = $report.Verdict; mutation = $record.Mutation; f3 = $report.F3 }
            Write-Host ("{0,-44} exit {1} (required {2}) {3}" -f "$label-$caseId", $exit, $record.ExpectedExit, $(if ($exit -eq $record.ExpectedExit) { 'as required' } else { 'NOT AS REQUIRED' }))
        }
        $summary["original-unchanged:$label"] = ((Sha $manifest.sql) -eq $manifest.sqlSha256) -and ((Sha $manifest.eso) -eq $manifest.esoSha256) -and ((Sha (Join-Path $source 'b1a.json')) -eq $manifest.b1aSha256)
    }
}

$summary.finished = (Get-Date).ToUniversalTime().ToString('o')
$summary | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (Join-Path $Root "summary-$Phase.json")
