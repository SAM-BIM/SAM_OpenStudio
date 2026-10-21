# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

<#
.SYNOPSIS
  S09 OpenStudio native-input read-back acceptance (no simulation, no CLI, no Tas).

.DESCRIPTION
  -Phase acceptance: checks the S08 signed-off fixture hashes (Fixtures\SHA256SUMS.txt) and the Budapest EPW hash first,
  then for s08-fix-box, s08-fix-two and a repeat of s08-fix-box: export (writer + SDK translation), read (a fresh
  process), compare under the approved rules (the verdict) and the provisional rules (baseline).
  -Phase acceptance-negative: every targeted mutation on a disposable copy of an acceptance run; each must FAIL (exit 10).
  Writes summary.json under -Root. Raw files stay under -Root (machine-local).
#>
param(
    [ValidateSet('acceptance', 'acceptance-negative')]
    [string]$Phase = 'acceptance',
    [string]$Root = 'C:\TasOut\s09\acc1',
    [Parameter(Mandatory = $true)][string]$WeatherEpw,
    [string]$WeatherEpwSha256 = '472ffb4e2f10867600949a392ce46891fc1bd05f2fc8d8917a99bca7d26209c3',
    [string]$Exe = (Join-Path $PSScriptRoot '..\SAM.Analytical.OpenStudio.InputReadBack.Cli\bin\x64\Release\net8.0\os-input-readback.exe')
)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$fixtures = Join-Path $PSScriptRoot 'Fixtures'

function Invoke-Step([string[]]$Arguments, [string]$Log) {
    & $Exe @Arguments *>> $Log
    return $LASTEXITCODE
}

function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

$summary = [ordered]@{
    phase       = $Phase
    started     = (Get-Date).ToUniversalTime().ToString('o')
    harnessDll  = Get-Sha256 ($Exe -replace '\.exe$', '.dll')
    runs        = @()
}

if ($Phase -eq 'acceptance') {
    if (Test-Path $Root) { throw "Root must be new: $Root" }
    New-Item -ItemType Directory -Path $Root | Out-Null

    foreach ($line in Get-Content (Join-Path $fixtures 'SHA256SUMS.txt')) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $hash, $name = $line -split '\s+', 2
        $actual = Get-Sha256 (Join-Path $fixtures $name.Trim())
        if ($actual -ne $hash.ToLowerInvariant()) { throw "Fixture $name hash $actual is not the signed-off $hash" }
        $summary["fixture:$($name.Trim())"] = $actual
    }

    $epwHash = Get-Sha256 $WeatherEpw
    if ($epwHash -ne $WeatherEpwSha256) { throw "EPW hash $epwHash is not $WeatherEpwSha256" }
    $summary.epw = $epwHash

    foreach ($run in @(@('s08-fix-box', 'box'), @('s08-fix-two', 'two'), @('s08-fix-box', 'box-repeat'))) {
        $fixtureId, $label = $run
        $dir = Join-Path $Root $label
        $log = Join-Path $Root "$label.console.txt"
        $export = Invoke-Step @('export', '--model', (Join-Path $fixtures "$fixtureId.json"), '--epw', $WeatherEpw, '--out', $dir, '--label', $label) $log
        if ($export -ne 0) { throw "export $label exit $export" }
        $read = Invoke-Step @('read', '--run', $dir) $log
        if ($read -ne 0) { throw "read $label exit $read" }
        $approved = Invoke-Step @('compare', '--run', $dir, '--fixture-id', $fixtureId, '--rule-set', 'approved', '--out', (Join-Path $dir 'compare-approved')) $log
        $provisional = Invoke-Step @('compare', '--run', $dir, '--fixture-id', $fixtureId, '--rule-set', 'provisional', '--out', (Join-Path $dir 'compare-provisional')) $log
        $report = Get-Content (Join-Path $dir 'compare-approved\report.json') -Raw | ConvertFrom-Json
        $summary.runs += [ordered]@{ label = $label; fixture = $fixtureId; approvedExit = $approved; provisionalExit = $provisional; status = $report.Status; coverage = $report.Coverage; required = $report.Required; results = $report.Results }
        Write-Host ("{0,-12} approved exit {1} ({2}, coverage {3}, {4}/{5}); provisional exit {6}" -f $label, $approved, $report.Status, $report.Coverage, $report.Results, $report.Required, $provisional)
    }

    # Control: the repeat export of the box must give byte-identical native files.
    foreach ($file in @('model.osm', 'in.idf')) {
        $a = Get-Sha256 (Join-Path $Root "box\$file")
        $b = Get-Sha256 (Join-Path $Root "box-repeat\$file")
        $summary["repeat:$file"] = if ($a -eq $b) { 'identical' } else { "DIFFERENT $a $b" }
    }
}
else {
    $cases = @(
        @('neg-identity-swap-names', 'two', 's08-fix-two'),
        @('neg-schedule-swap', 'box', 's08-fix-box'),
        @('neg-v1-unscheduled', 'two', 's08-fix-two'),
        @('neg-missing-dsoa-link', 'box', 's08-fix-box'),
        @('neg-missing-schedule-object', 'box', 's08-fix-box'),
        @('neg-people-density', 'box', 's08-fix-box'),
        @('neg-layer-conductivity', 'two', 's08-fix-two'),
        @('neg-osm-dsoa-schedule', 'box', 's08-fix-box')
    )
    $before = @{}
    foreach ($source in @('box', 'two')) { $before[$source] = Get-Sha256 (Join-Path $Root "$source\in.idf") }

    foreach ($case in $cases) {
        $caseId, $source, $fixtureId = $case
        $dir = Join-Path $Root "negatives\$caseId"
        $log = Join-Path $Root "negatives\$caseId.console.txt"
        New-Item -ItemType Directory -Force -Path (Split-Path $dir) | Out-Null
        if ((Invoke-Step @('negative', '--run', (Join-Path $Root $source), '--case', $caseId, '--out', $dir) $log) -ne 0) { throw "negative $caseId" }
        if ((Invoke-Step @('read', '--run', $dir) $log) -ne 0) { throw "read $caseId" }
        $exit = Invoke-Step @('compare', '--run', $dir, '--fixture-id', $fixtureId, '--rule-set', 'approved', '--out', (Join-Path $dir 'compare-approved')) $log
        $report = Get-Content (Join-Path $dir 'compare-approved\report.json') -Raw | ConvertFrom-Json
        $summary.runs += [ordered]@{ label = $caseId; fixture = $fixtureId; approvedExit = $exit; status = $report.Status; mutation = $report.Inputs.mutation; required = 10 }
        Write-Host ("{0,-28} exit {1} ({2}) {3}" -f $caseId, $exit, $report.Status, ($(if ($exit -eq 10) { 'FAIL as required' } else { 'NOT REFUSED' })))
    }

    foreach ($source in @('box', 'two')) {
        $summary["original-unchanged:$source"] = (Get-Sha256 (Join-Path $Root "$source\in.idf")) -eq $before[$source]
    }
}

$summary.finished = (Get-Date).ToUniversalTime().ToString('o')
$summary | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $Root "summary-$Phase.json")
