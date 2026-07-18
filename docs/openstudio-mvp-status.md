# OpenStudio MVP — run status (handover file)

Updated at every milestone gate so any fresh session can resume from the last
completed milestone. See [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md) §13.

## Current state

| Field | Value |
| --- | --- |
| Milestone completed | **M5 — profiles, schedules, internal conditions, space loads** |
| Branch | `feature/analytical-model-to-openstudio-mvp` (base `sow/2026-Q3` @ 94dfce9) |
| Commit | the commit introducing this change (SHA backfilled in the table below at the next gate) |
| SDK selected | OpenStudio NuGet **3.10.0** (bumped from 3.8.0 in all three library projects) |
| CLI selected | **3.10.0+86d7e215a1** — `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` (discovery: explicit → PATH → direct installs → ladybug_tools) |
| Tests executed | 44/44 passed — smoke (2) + M1 (12) + M2 (10) + M3 (7) + M4 (7) + M5 internal conditions (6) |
| Next milestone | **M6 — thermostats, Ideal Loads, weather, runner** (first E2E EnergyPlus simulation; strengthened result gate) |

## Milestone history

| M | Commit | Tests | Notes |
| --- | --- | --- | --- |
| M0 | d3a4a17 | 2/2 | SDK 3.8.0→3.10.0; CLI verified; tests project created; weather fixture pinned; placeholder `Test.cs` removed |
| M1 | 2495b5c | 14/14 | Diagnostics (+codes/severity), conversion/run options, run result, object reference/map, context + result snapshot, SanitizeName/OpenStudioName/versions queries |
| M2 | 0c1c5cb | 24/24 | SAM.Geometry.OpenStudio: Point3d/Point3dVector/Face3D-polygon converters; CleanVertices, Normal (Newell), Area, IsPlanar, IsClockwise, ValidatePolygon; SAM.Core+Geometry refs added to csproj |
| M3 | 40bad29 | 31/31 | ToOpenStudio orchestrator (passes A/B/C): stories from MinElevationDictionary, Space+ThermalZone, UpdateNormals-driven surfaces, explicit SAM-topology adjacency pairing, boundary conditions, SubSurfaces (window/door/glass-door rules), shading group; two-box fixture + 7 semantic tests. Learned: OpenStudio C# Space.surfaces/floorArea/volume are properties |
| M4 | 3f24d2c | 38/38 | Layer convention VERIFIED (SAM stores inside→outside per SAM XML docs; forward=E+ outside-first=reversed SAM list); Material/Construction converters with (Guid,thickness) and (Guid,direction) caches; Pass D assigns forward/reverse per internal side, AirBoundary for Air panels, pane constructions on subsurfaces; mapping doc; fixture with real 3-layer wall + double glazing; SPDX headers aligned to LGPL-3.0-or-later. Learned: SAM Material Guid-ctor order is (k, ρ, c); ConstructionLayer members live on SAM.Architectural base |
| M5 | (this commit) | 44/44 | Profile→ScheduleFixedInterval (8760 h, TimeSeries+Vector, typed limits, tiling rules) and InternalCondition→SpaceType (People incl. activity level+sensible fraction, Lights, ElectricEquipment, infiltration per sun-exposed area, per-space DSOA); IC mapping doc. Learned: Space.InternalCondition setter clones with NEW Guid → SpaceType dedup by sanitized name (LadybugTools parity); Profile.Count is the index span → use Min..Max indexer; ExposedToSun excludes plain PanelType.Wall (fixture uses WallExternal) |

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
