# OpenStudio MVP — run status (handover file)

Updated at every milestone gate so any fresh session can resume from the last
completed milestone. See [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md) §13.

## Current state

| Field | Value |
| --- | --- |
| Milestone completed | **M8 + independent review (Stage 3/4) — all P0/P1 findings fixed; MVP COMPLETE pending the human Rhino 8 smoke test** |
| Branch | `feature/analytical-model-to-openstudio-mvp` (base `sow/2026-Q3` @ 94dfce9) |
| Commit | `8c3367f` fix: sanitize quotes out of OpenStudio names (review P2-02) |
| SDK selected | OpenStudio NuGet **3.10.0** (bumped from 3.8.0 in all three library projects) |
| CLI selected | **3.10.0+86d7e215a1** — `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` (discovery: explicit → PATH → direct installs → ladybug_tools) |
| Tests executed | **73/73 passed** — incl. two E2E EnergyPlus runs + the CLI-timeout regression run; all projects compile with 0 errors |
| Review | [openstudio-mvp-review.md](openstudio-mvp-review.md) — 0 P0, 5 P1 and 2 P2 findings, all resolved with regression tests |
| Next milestone | Stage 5 — **human-assisted Rhino 8 smoke test** (plan §13), then PR into `sow/2026-Q3` |

## Milestone history

| M | Commit | Tests | Notes |
| --- | --- | --- | --- |
| M0 | d3a4a17 | 2/2 | SDK 3.8.0→3.10.0; CLI verified; tests project created; weather fixture pinned; placeholder `Test.cs` removed |
| M1 | 2495b5c | 14/14 | Diagnostics (+codes/severity), conversion/run options, run result, object reference/map, context + result snapshot, SanitizeName/OpenStudioName/versions queries |
| M2 | 0c1c5cb | 24/24 | SAM.Geometry.OpenStudio: Point3d/Point3dVector/Face3D-polygon converters; CleanVertices, Normal (Newell), Area, IsPlanar, IsClockwise, ValidatePolygon; SAM.Core+Geometry refs added to csproj |
| M3 | 40bad29 | 31/31 | ToOpenStudio orchestrator (passes A/B/C): stories from MinElevationDictionary, Space+ThermalZone, UpdateNormals-driven surfaces, explicit SAM-topology adjacency pairing, boundary conditions, SubSurfaces (window/door/glass-door rules), shading group; two-box fixture + 7 semantic tests. Learned: OpenStudio C# Space.surfaces/floorArea/volume are properties |
| M4 | 3f24d2c | 38/38 | Layer convention VERIFIED (SAM stores inside→outside per SAM XML docs; forward=E+ outside-first=reversed SAM list); Material/Construction converters with (Guid,thickness) and (Guid,direction) caches; Pass D assigns forward/reverse per internal side, AirBoundary for Air panels, pane constructions on subsurfaces; mapping doc; fixture with real 3-layer wall + double glazing; SPDX headers aligned to LGPL-3.0-or-later. Learned: SAM Material Guid-ctor order is (k, ρ, c); ConstructionLayer members live on SAM.Architectural base |
| M5 | ee06480 | 44/44 | Profile→ScheduleFixedInterval (8760 h, TimeSeries+Vector, typed limits, tiling rules) and InternalCondition→SpaceType (People incl. activity level+sensible fraction, Lights, ElectricEquipment, infiltration per sun-exposed area, per-space DSOA); IC mapping doc. Learned: Space.InternalCondition setter clones with NEW Guid → SpaceType dedup by sanitized name (LadybugTools parity); Profile.Count is the index span → use Min..Max indexer; ExposedToSun excludes plain PanelType.Wall (fixture uses WallExternal) |
| M8 | 1a27638 | 56/56 | GH components SAMAnalytical.ToOpenStudio + OpenStudio.RunModel (thin, GH_SAMVariableOutputParameterComponent pattern, _run-gated); runner save-only mode + standalone OSM/OSW Run API; usage doc; MVP acceptance checklist below |
| M7 | 8658945 | 56/56 | TwoStackedBoxes (stories 0m/3m, shared floor RoofCeiling/Floor flip), conditioned/unconditioned pair, irregular-planar cleaning, degenerate-panel kernel-failure isolation (SAM Face3D NRE → space-level GEO-001 error + per-panel no-silent-drop warnings), no-source-mutation, deterministic names, disposal loop, two-zone E2E. Hardened: per-panel elevation with exception isolation + floor-group story filter; UpdateNormals/panel-conversion/GetArea guards. Restored externally wiped SAMuild and SAM_SQLiteuild prebuilts (SAM repo moved to 487bd679) |
| M6 | 1015180 | 48/48 | Central IsConditioned; dual-setpoint thermostats with hourly heating≤cooling validation; ZoneHVACIdealLoadsAirSystem (E+ defaults, documented); EPW weather + Site from EpwFile; annual RunPeriod, 6 timesteps/h, no sizing runs, 6 hourly output variables; OpenStudioSimulationRunner (OSM save, minimal OSW, CLI discovery, timeout, err parse, SQL discovery, kWh extraction via DISTINCT KeyValue sums); full-pipeline ToOpenStudio(model, epw, outDir) overload. E2E: 2621.7 kWh heating / 1745.0 kWh cooling, Boston TMYx |
| **Review fixes** | `4d28513` … `8c3367f` (branch tip) | **73/73** | Independent review ([openstudio-mvp-review.md](openstudio-mvp-review.md)): 0 P0, 5 P1, 2 P2 — all fixed, one commit each: P1-01 weekly profiles now align to the run calendar (EPW start day of week / `OpenStudioConversionOptions.FirstDayOfWeek`); P1-02 leap-year/multi-day profiles no longer silently averaged (8760 truncation + warning; SAM-native tiling); P1-03 SpaceType dedup is name + content hash (same-name/different-content conditions never merge); P1-04 glazing optical sides map SAM External→E+ Front (LadybugTools deviation, documented); P1-05 CLI timeout actually fires (async pipe reads, Job-Object process-tree kill); P2-01 self-intersecting polygons rejected; P2-02 quotes sanitized out of OpenStudio names |

## Known limitations at this point

- Pre-existing benign `MSB3277` System.Memory warning (see audit doc §6).
- `Create.Model` (pre-existing) loads OSM without `VersionTranslator` — fine while SDK == CLI (3.10.0 both).
- DDY design-day import not implemented (optional per plan; Ideal Loads annual runs need no sizing periods).
- Schedules are 365-day hourly `ScheduleFixedInterval`; profiles longer than 8760 hours (leap years) are truncated to the first 8760 hours with a warning (documented non-leap policy); day-composed weekly profiles align to the run calendar (EPW-derived or explicit option; Monday-first fallback for weather-free conversions).
- Latent equipment gains and humidification/dehumidification setpoints not converted (documented warnings).
- Heating-only / cooling-only conditioning unsupported (both setpoint profiles required) — SAM_LadybugTools parity.
- Ideal Loads uses EnergyPlus object defaults; no capacity/air-flow limits, economizer, heat recovery or humidity control.
- **Environment note (this machine only):** an unrelated concurrent process rebuilt the SAM suite and started Rhino 8 (pid 5412) with the SAM GHAs loaded from `%APPDATA%\SAM`; while it runs, the *pre-existing* post-build deploy copy fails (`MSB3073`, locked `.gha` targets) and `SAM\build` / `SAM_SQLite\build` prebuilts were twice wiped (rebuilt by this session per the brief; sibling sources untouched). All projects compile; the full-solution build is green on a machine without that Rhino session.

## MVP acceptance checklist (plan §4 definition of success)

| # | Criterion | Status | Evidence |
| --- | --- | --- | --- |
| 1 | Convert to OpenStudio Model | DONE | M3–M5; 73-test suite |
| 2 | Save a valid .osm | DONE | M0 smoke CLI round-trip; M6 E2E |
| 3 | Generate an .osw | DONE | M6 runner; E2E artifacts |
| 4 | Run through the OpenStudio CLI | DONE | Two E2E runs (one-zone, two-zone) + three independent validation runs |
| 5 | EnergyPlus completes, no fatal errors | DONE | 0 fatal, 0 severe in all runs |
| 6 | Finite heating/cooling results (strengthened gate) | DONE | 2621.7 / 1745.0 kWh; both > 0; zone result count == conditioned count; kWh units validated against raw SQL |
| 7 | Floor area and volume preserved | DONE | 20 m² / 60 m³ exact (1e-6) |
| 8 | Internal surface relationships preserved | DONE | Explicit pairing, opposite normals, clean two-zone E+ run |
| 9 | Construction layer order preserved | DONE | Outside-first proven on both sides of the internal panel |
| 10 | Schedules and internal gains reproduced | DONE | 8760-value spot checks; weekly-profile calendar alignment tested; density assertions |
| 11 | Actionable diagnostics, no silent substitution | DONE | SAM-OS-* error paths tested; no-silent-drop panel check; kernel-failure isolation; leap-truncation warning; profile-gap error |

Remaining before merge (workflow stages): **human Rhino 8 smoke test** (Stage 5, plan §13),
then PR into sow/2026-Q3.

## Resume commands

```
cd "C:\Users\Virtual Machine\Documents\GitHub\SAM-BIM\SAM_OpenStudio"
git checkout feature/analytical-model-to-openstudio-mvp
dotnet build SAM_OpenStudio.sln -c Debug -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Debug -p:Platform=x64
```

If `SAM\build` or `SAM_SQLite\build` prebuilt DLLs are missing again (they were twice wiped by a
concurrent process on this machine), rebuild WITHOUT touching sibling source:

```
dotnet build ..\SAM\SAM.sln -c Debug
dotnet build ..\SAM_SQLite\SAM_SQLite.sln -c Debug
```

Then perform the human Rhino 8 smoke test per plan §13: Rhino 8 starts → Grasshopper loads the
GHA → `SAMAnalytical.ToOpenStudio` and `OpenStudio.RunModel` appear → one-zone conversion without
native-DLL errors → simulation starts without assembly-resolution errors. Reference material:
audit doc [openstudio-mvp-audit.md](openstudio-mvp-audit.md); review doc
[openstudio-mvp-review.md](openstudio-mvp-review.md); LadybugTools semantic reference paths in
plan §1; binding surface can be inspected via reflection on
`%USERPROFILE%\.nuget\packages\openstudio\3.10.0\build\netstandard2.0\x64\OpenStudio.dll`.
