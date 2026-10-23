# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

# Usage: run-ab.ps1 -Bin <dir with os-input-readback.exe> -Out <dir>. Each input under .\inputs is byte-copied to
# <Out>\<input>, then read and compare (approved rules) run as fresh processes. Prints one summary line per input.
param([string]$Bin, [string]$Out)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $Bin 'os-input-readback.exe'
foreach ($in in Get-ChildItem (Join-Path $PSScriptRoot 'inputs') -Directory | Sort-Object Name) {
    $dir = Join-Path $Out $in.Name
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory $dir | Out-Null
    foreach ($f in 'frozen-model.json', 'weather.epw', 'model.osm', 'in.idf', 'export.json') { Copy-Item (Join-Path $in.FullName $f) $dir }
    $fixture = if ($in.Name -like 'box*') { 's08-fix-box' } else { 's08-fix-two' }
    & $exe read --run $dir > (Join-Path $dir 'read.console.txt') 2>&1
    $readExit = $LASTEXITCODE
    & $exe compare --run $dir --fixture-id $fixture --rule-set approved --out (Join-Path $dir 'compare-approved') > (Join-Path $dir 'compare.console.txt') 2>&1
    $exit = $LASTEXITCODE
    $r = Get-Content -Raw (Join-Path $dir 'compare-approved\report.json') | ConvertFrom-Json
    $counts = ($r.CountByOutcome.PSObject.Properties | ForEach-Object { "$($_.Name) $($_.Value)" }) -join ', '
    "{0}: read {1}; compare exit {2}; {3}; {4} {5}/{6}; Consistency {7}; Unsupported {8}; {9}" -f $in.Name, $readExit, $exit, $r.Status, $r.Coverage, $r.Results, $r.Required, @($r.Consistency).Count, @($r.Unsupported).Count, $counts
    foreach ($c in @($r.Consistency)) { "    Consistency: $c" }
    foreach ($u in @($r.Unsupported)) { "    Unsupported: $u" }
}
