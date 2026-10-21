# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

# Usage: pid-reuse.ps1 -Exe <os-input-readback.exe> [-Count 200]. Launches the harness (no arguments: usage, exit 64)
# -Count times in sequence, as TestSupport.Cli does (redirected output, wait, dispose), and prints how many process ids
# Windows handed out again and how many launches apart.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Count = 200
)

$ErrorActionPreference = 'Stop'
$ids = New-Object System.Collections.Generic.List[int]
for ($i = 0; $i -lt $Count; $i++) {
    $info = New-Object System.Diagnostics.ProcessStartInfo $Exe
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $null = $process.StandardOutput.ReadToEnd()
    $null = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $ids.Add($process.Id)
    $process.Dispose()
}

$reuses = @()
for ($i = 1; $i -lt $ids.Count; $i++) {
    $previous = $ids.LastIndexOf($ids[$i], $i - 1)
    if ($previous -ge 0) { $reuses += "pid $($ids[$i]): launch $previous and $i ($($i - $previous) apart)" }
}

"launches $($ids.Count); distinct ids $(($ids | Select-Object -Unique).Count); reused $($reuses.Count)"
$reuses
