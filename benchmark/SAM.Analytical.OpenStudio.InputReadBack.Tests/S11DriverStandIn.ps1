# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors
#
# Offline stand-in for os-results-readback, used only by S11DriverTests: Run-S11-Results.ps1 starts it in place of the
# harness. It starts no engine, loads no model and reads no SQL/ESO; it writes the files the real steps write, with the
# shapes the driver checks. scenario.json beside it sets, per "<directory name>|<step>", what a step does:
#   exit (int)            the exit code (default: 0; compare: the verdict the case requires, 0 for a run)
#   write (bool)          false: write nothing (default true)
#   verdict (string)      the report verdict (default: from the exit)
#   wrongInputs (bool)    the report records another SQL hash
#   od7 (bool)            F3.MeetsOd7 (default true)
#   touch (string)        a file to append to (e.g. an original the step must not change)
#   mutate (bool)         negative: false copies without mutating (default true)
#   expectedExit (int)    negative: the exit negative.json records (default: the case's)
# Every call is appended to calls.txt beside it.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Items)

$step = $Items[0]
$options = @{}
for ($i = 1; $i + 1 -lt $Items.Count; $i += 2) { $options[$Items[$i].TrimStart('-')] = $Items[$i + 1] }
$directory = $(if ($step -eq 'negative') { $options['out'] } else { $options['run'] })
$name = Split-Path $directory -Leaf
Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value "$name|$step"
$scenario = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'scenario.json') -Raw | ConvertFrom-Json
$entry = $scenario."$name|$step"

function Sha([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

function Option($Name, $Default) {
    if ($entry -and $null -ne $entry.$Name) { return $entry.$Name }
    return $Default
}

$cases = @{ 'ctl-unmutated-copy' = 0; 'neg-delete-hour-both' = 11; 'neg-duplicate-hour-both' = 11; 'neg-sensible-cooling-substitution' = 10 }
$case = @($cases.Keys | Where-Object { $name.EndsWith('-' + $_) })[0]
$defaultExit = 0
if ($step -eq 'compare' -and $case) { $defaultExit = $cases[$case] }
$exit = [int](Option 'exit' $defaultExit)
$touch = Option 'touch' $null
if ($touch) { Add-Content -LiteralPath $touch -Value 'changed by a step' }

if (Option 'write' $true) {
    $stamp = [Guid]::NewGuid().ToString('N')
    switch ($step) {
        'read' { [IO.File]::WriteAllText((Join-Path $directory 'reference.json'), "{ ""stand-in"": ""read $stamp"" }") }
        'subject' { [IO.File]::WriteAllText((Join-Path $directory 'subject.json'), "{ ""stand-in"": ""subject $stamp"" }") }
        'negative' {
            $source = $options['run']
            New-Item -ItemType Directory -Force -Path (Join-Path $directory 'work\run') | Out-Null
            foreach ($file in @('b1a.json', 'sam.json', 'work\run\in.idf', 'work\run\eplusout.sql', 'work\run\eplusout.eso')) {
                Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $directory $file)
            }
            $mutation = 'none (control)'
            if (Option 'mutate' $true) {
                if ($case -eq 'neg-delete-hour-both' -or $case -eq 'neg-duplicate-hour-both') {
                    Add-Content -LiteralPath (Join-Path $directory 'work\run\eplusout.sql') -Value $case
                    Add-Content -LiteralPath (Join-Path $directory 'work\run\eplusout.eso') -Value $case
                    $mutation = "$case on the SQL and ESO copies"
                }
                elseif ($case -eq 'neg-sensible-cooling-substitution') {
                    Add-Content -LiteralPath (Join-Path $directory 'b1a.json') -Value $case
                    Add-Content -LiteralPath (Join-Path $directory 'sam.json') -Value $case
                    [IO.File]::WriteAllText((Join-Path $directory 'subject.json'), "{ ""stand-in"": ""captured subject $stamp"" }")
                    $mutation = "$case on the b1a.json and sam.json copies"
                }
            }
            $record = [ordered]@{ Case = $case; Source = $source; ExpectedExit = [int](Option 'expectedExit' $cases[$case]); Mutation = $mutation }
            [IO.File]::WriteAllText((Join-Path $directory 'negative.json'), ($record | ConvertTo-Json))
        }
        'compare' {
            $verdict = Option 'verdict' $(switch ($exit) { 0 { 'PASS' } 10 { 'FAIL' } 11 { 'BLOCKED' } default { 'PASS' } })
            $inputs = [ordered]@{
                'model' = Sha $options['model']
                'in.idf' = Sha (Join-Path $directory 'work\run\in.idf')
                'eplusout.sql' = $(if (Option 'wrongInputs' $false) { 'feedface' } else { Sha (Join-Path $directory 'work\run\eplusout.sql') })
                'eplusout.eso' = Sha (Join-Path $directory 'work\run\eplusout.eso')
                'b1a.json' = Sha (Join-Path $directory 'b1a.json')
                'sam.json' = Sha (Join-Path $directory 'sam.json')
                'reference.json' = Sha (Join-Path $directory 'reference.json')
                'subject.json' = Sha (Join-Path $directory 'subject.json')
            }
            $failed = $(if ($verdict -eq 'FAIL') { 1 } else { 0 })
            $blocked = $(if ($verdict -eq 'BLOCKED') { 1 } else { 0 })
            $report = [ordered]@{
                Schema = 's11-compare/1'; Verdict = $verdict; ExitCode = $exit; Required = 85; Passed = 85 - $failed - $blocked; Failed = $failed; Blocked = $blocked
                Inputs = $inputs; F3 = [ordered]@{ MeetsOd7 = [bool](Option 'od7' $true) }
            }
            New-Item -ItemType Directory -Force -Path (Join-Path $directory 'compare') | Out-Null
            [IO.File]::WriteAllText((Join-Path $directory 'compare\compare.json'), ($report | ConvertTo-Json -Depth 4))
        }
    }
}

exit $exit
