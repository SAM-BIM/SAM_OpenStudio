# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

# Usage: simulate-pid-reuse.ps1 -Exe <os-input-readback.exe> -Model <s08-fix-box.json> -Epw <budapest.epw> -Out <new dir>
# [-Repeat 3]. With one binary: export the box fixture, then per repeat two neg-people-density copies, each read in a
# fresh process. "control" is compared as made; "reused" first gets the reader's process id written into its export.json,
# the state Windows id reuse produces (the export process exited long before the reader started). Prints one line per
# step: exit code, time, the first output line, and whether compare wrote compare-approved.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Model,
    [Parameter(Mandatory = $true)][string]$Epw,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$Repeat = 3
)

$ErrorActionPreference = 'Stop'
if (Test-Path $Out) { throw "Out must be new: $Out" }
New-Item -ItemType Directory -Path $Out | Out-Null
$lines = New-Object System.Collections.Generic.List[string]

function Step([string]$Label, [string[]]$Arguments) {
    # The harness reports refusals on stderr; under Windows PowerShell 5.1 that must not stop the script.
    $ErrorActionPreference = 'Continue'
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & $Exe @Arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    $watch.Stop()
    $lines.Add(("{0,-22} exit {1,3} {2,6} ms  {3}" -f $Label, $code, $watch.ElapsedMilliseconds, ($output.Trim() -split "`r?`n")[0]))
}

$source = Join-Path $Out 'source'
Step 'export' @('export', '--model', $Model, '--epw', $Epw, '--out', $source, '--label', 'box')
foreach ($k in 1..$Repeat) {
    foreach ($kind in 'control', 'reused') {
        $run = Join-Path $Out "$kind$k"
        Step "negative $kind$k" @('negative', '--run', $source, '--case', 'neg-people-density', '--out', $run)
        Step "read $kind$k" @('read', '--run', $run)
        if ($kind -eq 'reused') {
            $manifest = Join-Path $run 'export.json'
            $readerId = (Get-Content (Join-Path $run 'native.json') -Raw | ConvertFrom-Json).ProcessId
            [System.IO.File]::WriteAllText($manifest, [regex]::Replace((Get-Content $manifest -Raw), '"ProcessId":\s*\d+', '"ProcessId": ' + $readerId))
        }

        Step "compare $kind$k" @('compare', '--run', $run, '--fixture-id', 's08-fix-box', '--rule-set', 'approved', '--out', (Join-Path $run 'compare-approved'))
        $lines.Add("$kind$k compare-approved written: $(Test-Path (Join-Path $run 'compare-approved'))")
    }
}

$lines | Set-Content -Path (Join-Path $Out 'summary.txt') -Encoding utf8
$lines
