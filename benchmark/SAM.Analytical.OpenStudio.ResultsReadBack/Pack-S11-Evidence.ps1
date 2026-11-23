# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

<#
.SYNOPSIS
  Copies the S11 acceptance evidence from -Root into the repository (documentation/S11-OpenStudio-ResultsRoundTrip-evidence).

.DESCRIPTION
  Copies the text evidence only: run manifests, b1a.json, sam.json, reference.json, subject.json, compare.json/.md, the
  negatives' negative.json and compare output, console logs, the phase summaries and the build receipt, then writes
  SHA256SUMS.txt over the copied files. eplusout.sql, eplusout.eso and the EnergyPlus work folders stay machine-local; the
  manifests record their paths and hashes.
#>
param(
    [string]$Root = 'C:\TasOut\s11\acc1',
    [Parameter(Mandatory = $true)][string]$Label,
    [string]$BuildReceipt = ''
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $BuildReceipt) { $BuildReceipt = Join-Path $repository 'benchmark\SAM.Analytical.OpenStudio.ResultsReadBack.Cli\bin\x64\Release\net8.0\s11-build-receipt.json' }
$target = Join-Path $repository "documentation\S11-OpenStudio-ResultsRoundTrip-evidence\$Label"
if (Test-Path -LiteralPath $target) { throw "The evidence folder exists already: $target" }

function Sha([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

$include = @('run-manifest.json', 'b1a.json', 'sam.json', 'reference.json', 'subject.json', 'negative.json', 'compare.json', 'compare.md', 'summary-*.json', '*.console.txt', '*.readback.txt')
$copied = New-Object System.Collections.Generic.List[string]
foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
    $relative = $file.FullName.Substring($Root.TrimEnd('\').Length + 1)
    if ($relative -match '(^|\\)work\\') { continue }
    $match = $false
    foreach ($pattern in $include) { if ($file.Name -like $pattern) { $match = $true } }
    if (-not $match) { continue }
    $destination = Join-Path $target $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    $copied.Add($relative)
}

if (Test-Path -LiteralPath $BuildReceipt) {
    Copy-Item -LiteralPath $BuildReceipt -Destination (Join-Path $target 's11-build-receipt.json')
    $copied.Add('s11-build-receipt.json')
}

$sums = @($copied | Sort-Object | ForEach-Object { (Sha (Join-Path $target $_)) + '  ' + ($_ -replace '\\', '/') })
[IO.File]::WriteAllText((Join-Path $target 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
Write-Host "packed $($copied.Count) files into $target"
