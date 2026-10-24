# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors
#
# S11: the controlled, engine-free Release build whose receipt Run-S11-Results.ps1 requires (ported from SAM_Tas
# Build-S10-Receipt.ps1). Nothing here starts OpenStudio or EnergyPlus.
#
#   1. SAM, SAM_Validation, SAM_Tas and SAM_OpenStudio must each be a clean checkout (no change, no untracked file). Their
#      HEADs are the build commits.
#   2. MSBuild rebuilds, one project at a time, the SAM core libraries into SAM\build, the B1a schema into
#      SAM_Validation\build, then the two S11 executables: benchmark-openstudio (the producer) and os-results-readback
#      (the harness, which builds the S09 reader and the SAM_Tas evaluation core it references). Each build gets
#      /p:SAMSourceRevision=<its checkout's HEAD>, so every assembly carries InformationalVersion <version>+<commit>.
#      The Grasshopper projects are NOT built: their post-build step deploys into %APPDATA%\SAM.
#   3. Only after every build exits 0: the checkouts are still clean at the same HEADs; each declared artifact was written
#      by this build and carries its checkout's stamp; every SAM\build library beside the executables is byte-identical
#      to its SAM\build copy.
#   4. The receipt (default: s11-build-receipt.json beside os-results-readback.exe) records the commits, the commands and
#      the sha256 of every .dll/.exe in both executable folders. It is removed first, so a refused build leaves none.
#
# Usage (Windows PowerShell 5.1; the defaults are this workspace's paths):
#   powershell -ExecutionPolicy Bypass -File Build-S11-Receipt.ps1
param(
    [string]$MSBuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
    [string]$RunnerRepository = '',
    [string]$BuildReceipt = ''
)

$ErrorActionPreference = 'Stop'
if (-not $RunnerRepository) { $RunnerRepository = Join-Path $PSScriptRoot '..\..' }
$RunnerRepository = [IO.Path]::GetFullPath($RunnerRepository)
$workspace = Split-Path $RunnerRepository
$SamRepository = Join-Path $workspace 'SAM'
$ValidationRepository = Join-Path $workspace 'SAM_Validation'
$TasRepository = Join-Path $workspace 'SAM_Tas'
$benchmarkProject = Join-Path $RunnerRepository 'benchmark\SAM.Analytical.OpenStudio.Benchmark\SAM.Analytical.OpenStudio.Benchmark.csproj'
$harnessProject = Join-Path $RunnerRepository 'benchmark\SAM.Analytical.OpenStudio.ResultsReadBack.Cli\SAM.Analytical.OpenStudio.ResultsReadBack.Cli.csproj'
$benchmarkExe = Join-Path $RunnerRepository 'benchmark\SAM.Analytical.OpenStudio.Benchmark\bin\x64\Release\net8.0\benchmark-openstudio.exe'
$harnessExe = Join-Path $RunnerRepository 'benchmark\SAM.Analytical.OpenStudio.ResultsReadBack.Cli\bin\x64\Release\net8.0\os-results-readback.exe'
if (-not $BuildReceipt) { $BuildReceipt = Join-Path (Split-Path $harnessExe) 's11-build-receipt.json' }
$BuildReceipt = [IO.Path]::GetFullPath($BuildReceipt)

# The SAM core libraries the S11 executables load from SAM\build, in dependency order.
$samProjects = 'SAM.Core', 'SAM.Math', 'SAM.Units', 'SAM.Geometry', 'SAM.Weather', 'SAM.Architectural', 'SAM.Analytical'

function Refuse([string]$Reason) {
    Write-Host "REFUSED (no receipt written): $Reason"
    exit 3
}

# SHA-256 through .NET (Get-FileHash is a module function Windows PowerShell 5.1 may not autoload in a child host).
function Sha([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

$git = 'C:\Program Files\Git\cmd\git.exe'
if (-not (Test-Path $git)) { $git = 'git' }

function Git([string]$Directory) {
    $ErrorActionPreference = 'Continue'
    & $git -C $Directory @args 2>$null
}

function Clean-Head([string]$Directory) {
    if (-not (Test-Path (Join-Path $Directory '.git'))) { Refuse "$Directory is not a git checkout" }
    $head = (Git $Directory rev-parse HEAD | Out-String).Trim()
    if ($head -notmatch '^[0-9a-f]{40}$') { Refuse "HEAD of $Directory is not a full commit SHA ('$head')" }
    $status = @(Git $Directory status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { Refuse "git status failed in $Directory" }
    if ($status.Count -gt 0) { Refuse "$Directory has $($status.Count) uncommitted or untracked change(s), e.g. $($status[0])" }
    return $head
}

function Test-Stamp([string]$Path, [string]$Commit) {
    $text = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($Path))
    return $text.Contains('+' + $Commit)
}

if (Test-Path -LiteralPath $BuildReceipt) { Remove-Item -LiteralPath $BuildReceipt -Force }
$heads = [ordered]@{}
foreach ($repository in @($SamRepository, $ValidationRepository, $TasRepository, $RunnerRepository)) { $heads[$repository] = Clean-Head $repository }
if (-not (Test-Path -LiteralPath $MSBuild)) { Refuse "MSBuild not found: $MSBuild" }

$samBuild = Join-Path $SamRepository 'build'
$started = (Get-Date).ToUniversalTime()
$commands = New-Object System.Collections.Generic.List[object]

function Build([string]$Project, [string]$Commit, [string[]]$Extra) {
    $arguments = @($Project, '-restore', '-t:Rebuild', '-p:Configuration=Release', "-p:SAMSourceRevision=$Commit", '-nr:false', '-v:m') + $Extra
    Write-Host "==> $Project"
    & $MSBuild @arguments | Out-Host
    $code = $LASTEXITCODE
    $commands.Add([ordered]@{ file = $MSBuild; arguments = ($arguments -join ' '); exitCode = $code })
    if ($code -ne 0) { Refuse "MSBuild exited $code for $Project" }
}

foreach ($name in $samProjects) { Build (Join-Path $SamRepository "SAM\$name\$name.csproj") $heads[$SamRepository] @() }
Build (Join-Path $ValidationRepository 'SAM.Analytical.Benchmark\SAM.Analytical.Benchmark.csproj') $heads[$ValidationRepository] @()
Build $benchmarkProject $heads[$RunnerRepository] @('-p:Platform=x64')
Build $harnessProject $heads[$RunnerRepository] @('-p:Platform=x64')

foreach ($repository in @($heads.Keys)) {
    if ((Clean-Head $repository) -ne $heads[$repository]) { Refuse "the HEAD of $repository moved during the build" }
}

$benchmarkDir = Split-Path $benchmarkExe
$harnessDir = Split-Path $harnessExe
$stamped = New-Object System.Collections.Generic.List[object]
$declared = @(
    [ordered]@{ path = $benchmarkExe; commit = $heads[$RunnerRepository]; compiled = $false },
    [ordered]@{ path = [IO.Path]::ChangeExtension($benchmarkExe, '.dll'); commit = $heads[$RunnerRepository]; compiled = $true },
    [ordered]@{ path = $harnessExe; commit = $heads[$RunnerRepository]; compiled = $false },
    [ordered]@{ path = [IO.Path]::ChangeExtension($harnessExe, '.dll'); commit = $heads[$RunnerRepository]; compiled = $true },
    [ordered]@{ path = (Join-Path $harnessDir 'SAM.Analytical.OpenStudio.ResultsReadBack.dll'); commit = $heads[$RunnerRepository]; compiled = $true },
    [ordered]@{ path = (Join-Path $harnessDir 'SAM.Analytical.OpenStudio.InputReadBack.dll'); commit = $heads[$RunnerRepository]; compiled = $true },
    [ordered]@{ path = (Join-Path $harnessDir 'SAM.Analytical.OpenStudio.dll'); commit = $heads[$RunnerRepository]; compiled = $true },
    # The SAM_Tas evaluation core is compiled by the harness build (project reference), so it carries the runner's stamp;
    # its source commit is the clean SAM_Tas HEAD recorded under commits.
    [ordered]@{ path = (Join-Path $harnessDir 'SAM.Analytical.InputEquivalence.dll'); commit = $heads[$RunnerRepository]; compiled = $true },
    [ordered]@{ path = (Join-Path $ValidationRepository 'build\SAM.Analytical.Benchmark.dll'); commit = $heads[$ValidationRepository]; compiled = $true }
) + @($samProjects | ForEach-Object { [ordered]@{ path = (Join-Path $samBuild "$_.dll"); commit = $heads[$SamRepository]; compiled = $true } })
foreach ($artifact in $declared) {
    $item = Get-Item -LiteralPath $artifact.path -ErrorAction SilentlyContinue
    if (-not $item) { Refuse "the build did not produce $($artifact.path)" }
    if ($item.LastWriteTimeUtc -lt $started) { Refuse "$($artifact.path) was not written by this build" }
    if ($artifact.compiled) {
        if (-not (Test-Stamp $artifact.path $artifact.commit)) { Refuse "$($artifact.path) does not carry the stamp +$($artifact.commit)" }
        $stamped.Add([ordered]@{ path = $item.FullName; commit = $artifact.commit })
    }
}

foreach ($library in @(Get-ChildItem -LiteralPath $benchmarkDir, $harnessDir -Filter 'SAM.*.dll' | Where-Object { Test-Path -LiteralPath (Join-Path $samBuild $_.Name) })) {
    if ($samProjects -notcontains $library.BaseName) { Refuse "$($library.FullName) is a SAM\build library this build does not rebuild" }
    if ((Sha $library.FullName) -ne (Sha (Join-Path $samBuild $library.Name))) { Refuse "$($library.FullName) differs from $(Join-Path $samBuild $library.Name)" }
}

$files = @(Get-ChildItem -LiteralPath $benchmarkDir, $harnessDir -File | Where-Object { $_.Extension -in '.dll', '.exe' } | Sort-Object FullName | ForEach-Object { [ordered]@{ path = $_.FullName; sha256 = (Sha $_.FullName) } })

[ordered]@{
    schema = 's11-build-receipt/1'
    note = 'Written by Build-S11-Receipt.ps1 after a successful controlled build of clean checkouts; Run-S11-Results.ps1 verifies it and never writes it.'
    startedUtc = $started.ToString('o'); completedUtc = (Get-Date).ToUniversalTime().ToString('o'); machine = $env:COMPUTERNAME
    msbuild = $MSBuild; commands = $commands
    commits = @($heads.Keys | ForEach-Object { [ordered]@{ repository = $_; commit = $heads[$_]; branch = (Git $_ rev-parse --abbrev-ref HEAD | Out-String).Trim() } })
    benchmarkExe = $benchmarkExe; harnessExe = $harnessExe
    stamped = $stamped; files = $files
} | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $BuildReceipt

Write-Host "receipt: $BuildReceipt ($($files.Count) files)"
exit 0
