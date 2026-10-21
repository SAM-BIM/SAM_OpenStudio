# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

<#
.SYNOPSIS
  Copies the minimal, sanitized S09 acceptance evidence from a machine-local run root into the repository.

.DESCRIPTION
  Per run: export.json (paths reduced to file names), compare-approved\report.json and report.txt, and
  compare-provisional\report.txt. Per negative: report.json and report.txt. Plus the summary files. Native files (OSM,
  IDF, native.json) and console logs stay machine-local; their hashes are in export.json and report.json.
#>
param(
    [string]$Root = 'C:\TasOut\s09\acc1',
    [string]$Target = (Join-Path $PSScriptRoot '..\..\documentation\S09-OpenStudio-InputReadBack-evidence')
)

$ErrorActionPreference = 'Stop'
$rootFull = (Resolve-Path $Root).Path.TrimEnd('\')
$Target = [System.IO.Path]::GetFullPath($Target)
New-Item -ItemType Directory -Force -Path $Target | Out-Null

function Copy-Sanitized([string]$Source, [string]$Destination) {
    if (-not (Test-Path $Source)) { return }
    New-Item -ItemType Directory -Force -Path (Split-Path $Destination) | Out-Null
    $text = Get-Content -LiteralPath $Source -Raw
    foreach ($form in @($rootFull, $rootFull.Replace('\', '/'), $rootFull.Replace('\', '\\'))) { $text = $text.Replace($form, '<root>') }
    $text = [regex]::Replace($text, '[A-Za-z]:(\\\\|\\|/)[^"\r\n]*?(\\\\|\\|/)(?=[^\\/"\r\n]+")', '<local>/')
    [System.IO.File]::WriteAllText($Destination, $text, (New-Object System.Text.UTF8Encoding($false)))
}

foreach ($run in Get-ChildItem -Directory $rootFull | Where-Object { Test-Path (Join-Path $_.FullName 'export.json') }) {
    $out = Join-Path $Target "runs\$($run.Name)"
    Copy-Sanitized (Join-Path $run.FullName 'export.json') (Join-Path $out 'export.json')
    Copy-Sanitized (Join-Path $run.FullName 'compare-approved\report.json') (Join-Path $out 'report-approved.json')
    Copy-Sanitized (Join-Path $run.FullName 'compare-approved\report.txt') (Join-Path $out 'report-approved.txt')
    Copy-Sanitized (Join-Path $run.FullName 'compare-provisional\report.txt') (Join-Path $out 'report-provisional.txt')
}

$negatives = Join-Path $rootFull 'negatives'
if (Test-Path $negatives) {
    foreach ($case in Get-ChildItem -Directory $negatives) {
        $out = Join-Path $Target "negatives\$($case.Name)"
        Copy-Sanitized (Join-Path $case.FullName 'compare-approved\report.json') (Join-Path $out 'report-approved.json')
        Copy-Sanitized (Join-Path $case.FullName 'compare-approved\report.txt') (Join-Path $out 'report-approved.txt')
    }
}

foreach ($summary in Get-ChildItem -File $rootFull -Filter 'summary-*.json') {
    Copy-Sanitized $summary.FullName (Join-Path $Target $summary.Name)
}

Write-Host "evidence packed into $Target"
