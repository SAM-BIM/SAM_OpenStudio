# S06 - OpenStudio CLI selection matches the SDK - PR record

Roadmap task S06 (`SAM/documentation/SIMULATION_ROADMAP_Q4_2026.md`), supporting PR for SAM-BIM/SAM_Deploy's S06 smoke
candidate (branch `feature/s06-installed-smoke-candidate`). Base: `sow/2026-Q4` (`d4216f35`). Branch:
`fix/s06-openstudio-cli-sdk-match`. Validated content SHA: `1f4bf60c9b34dceef9d20310c403e2c44f3dcd4c` (code and tests).
Record-only commits follow; the final PR head and hosted checks are in the PR description.
Merge needs owner approval (AGENTS.md). `PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status
Ready for review. Local tests pass. The licensed installed-candidate smoke results (Tas, OpenStudio and Both from the
installed SAM_Deploy payload) are recorded in SAM_Deploy's S06 record, which consumes this commit through its
SAM_OpenStudio gitlink.

## Problem
`Query.OpenStudioCliPath()` with no explicit path returned the first `openstudio.exe` on `PATH`. Neither SAM_UI's
OpenStudio route nor `benchmark-openstudio` passes a path, so `PATH` decided which engine ran. On the S05 laptop the
in-process SDK is OpenStudio 3.10.0 (EnergyPlus 25.1.0), while the CLI otherwise found is ladybug_tools' OpenStudio 3.11.0
(EnergyPlus 25.2.0). S03 (SAM_UI#242) recorded this; S05 worked around it by putting 3.10 first on `PATH` in the harness.

## Change
`SAM_OpenStudio/SAM.Core.OpenStudio/Query/OpenStudioCliPath.cs`:
- Discovery order unchanged: explicit path; `PATH`; `<system drive>\openstudio*` and `%ProgramFiles%\OpenStudio*`, newest
  first; `%ProgramFiles%\ladybug_tools\openstudio`. Duplicate paths are listed once.
- No explicit path and more than one CLI found: the first whose `openstudio.exe --version` equals the loaded SDK version
  (`Query.OpenStudioVersion()`, build metadata after `+` ignored) is returned. No match, or the SDK version unknown: the
  first one found, as before. One or no CLI found: unchanged, no version probe.
- An explicit path is never version-checked (unchanged).
- New public helpers: `OpenStudioCliPath(candidates, sdkVersion, cliVersion)` (the pure selection rule) and
  `OpenStudioCliVersion(cliPath)` (`--version`, 30 s timeout, cached per path/size/write time).

Not changed: the SDK integration, the runner, diagnostics, conversion, results. A mismatch that remains (only a
different version installed) stays visible where S03 already records it: the OpenStudio run manifest's
`engine.openStudioSdkVersion`, `engine.energyPlusVersion` (from `eplusout.err`) and `engine.cli` (path, hash, version).

## Decisions and assumptions
- Prefer, do not refuse: refusing a non-matching CLI would stop users who only have another OpenStudio installed; that is
  an owner decision, not S06's minimal correction.
- Match on the full core version (`3.10.0`), not major.minor: the EnergyPlus build changes with the OpenStudio patch.

## Files changed
- `SAM_OpenStudio/SAM.Core.OpenStudio/Query/OpenStudioCliPath.cs`
- `benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/OpenStudioCliSelectionTests.cs` (new; 9 offline tests, in the
  project hosted CI runs)
- `documentation/S06-OpenStudio-Cli-Selection-PR.md` (this record)

## Validation (local, Windows, on `1f4bf60c`)
- `dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c
  Release -p:Platform=x64 --filter "TestCategory!=Simulation"`: **51/51 passed** (the 9 new included).
- Mutation check: disabling the version match (always the first candidate) fails 4 of the 9 new tests (preference, first
  match wins, build metadata, unknown version); restored.
- End to end: SAM_Deploy S06 record (installed candidate run with ladybug's 3.11 deliberately first on `PATH` and 3.10
  second).
- Hosted CI: see the PR description.

## Unresolved, risks
- Each run with more than one CLI installed starts `openstudio.exe --version` once per candidate per process (cached).
- `tests/SAM.Analytical.OpenStudio.Tests` (not run by hosted CI) was not rerun; it calls `OpenStudioCliPath()` only to
  locate a CLI.

## Next step
Owner review. After merge, repoint SAM_Deploy's S06 SAM_OpenStudio gitlink from `1f4bf60c` to the merge commit, then the
SAM_OpenStudio `PROJECT_PROGRESS.md` closeout.
