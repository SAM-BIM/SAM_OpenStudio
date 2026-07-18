# OpenStudio MVP — run status (handover file)

Updated at every milestone gate so any fresh session can resume from the last
completed milestone. See [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md) §13.

## Current state

| Field | Value |
| --- | --- |
| Milestone completed | **M1 — conversion contracts and diagnostics** |
| Branch | `feature/analytical-model-to-openstudio-mvp` (base `sow/2026-Q3` @ 94dfce9) |
| Commit | the commit introducing this change (SHA backfilled in the table below at the next gate) |
| SDK selected | OpenStudio NuGet **3.10.0** (bumped from 3.8.0 in all three library projects) |
| CLI selected | **3.10.0+86d7e215a1** — `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` (discovery: explicit → PATH → direct installs → ladybug_tools) |
| Tests executed | 14/14 passed — smoke (2) + naming (4) + object map (4) + diagnostics/context (4) |
| Next milestone | **M2 — geometry primitive conversion** (`SAM.Geometry.OpenStudio`: point/polygon conversion, vertex cleaning, planarity/area validation, 9 test cases) |

## Milestone history

| M | Commit | Tests | Notes |
| --- | --- | --- | --- |
| M0 | d3a4a17 | 2/2 | SDK 3.8.0→3.10.0; CLI verified; tests project created; weather fixture pinned; placeholder `Test.cs` removed |
| M1 | (this commit) | 14/14 | Diagnostics (+codes/severity), conversion/run options, run result, object reference/map, context + result snapshot, SanitizeName/OpenStudioName/versions queries |

## Known limitations at this point

- No conversion code exists yet — only toolchain, contracts land in M1.
- Pre-existing benign `MSB3277` System.Memory warning (see audit doc §6).
- `Create.Model` (pre-existing) loads OSM without `VersionTranslator` — fine while SDK == CLI; M6 runner will use `VersionTranslator`.

## Resume commands

```
cd "C:\Users\Virtual Machine\Documents\GitHub\SAM-BIM\SAM_OpenStudio"
git checkout feature/analytical-model-to-openstudio-mvp
dotnet build SAM_OpenStudio.sln -c Debug -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Debug -p:Platform=x64
```

Then continue with the next milestone per plan §13. Reference material:
audit doc [openstudio-mvp-audit.md](openstudio-mvp-audit.md); LadybugTools semantic
reference paths in plan §1; binding surface can be inspected via reflection on
`%USERPROFILE%\.nuget\packages\openstudio\3.10.0\build\netstandard2.0\x64\OpenStudio.dll`.
