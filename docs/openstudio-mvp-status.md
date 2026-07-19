# OpenStudio MVP — run status (handover file)

Updated at every milestone gate so any fresh session can resume from the last
completed milestone. See [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md) §13.

## Current state

The MVP is complete and validated end-to-end: **82/82 automated tests pass, the automated
opaque-air-gap EnergyPlus test passes, the real Rhino 8 model test passes, and all P0/P1
review findings are resolved. The branch is ready for PR into `sow/2026-Q3`.**

| Field | Value |
| --- | --- |
| Milestone completed | **M8 + independent review + P1-06 + real Rhino 8 validation — MVP COMPLETE, ready for PR** |
| Branch | `feature/analytical-model-to-openstudio-mvp` (base `sow/2026-Q3` @ 94dfce9) |
| Commit | `fd2c0a7` fix: distinguish opaque air gaps from window gas layers (review P1-06) |
| SDK selected | OpenStudio NuGet **3.10.0** (bumped from 3.8.0 in all three library projects) |
| CLI selected | **3.10.0+86d7e215a1** — `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` (discovery: explicit → PATH → direct installs → ladybug_tools) |
| Tests executed | **82/82 passed, 0 skipped** — incl. three E2E EnergyPlus runs + CLI-timeout regression + opaque-air-gap E2E |
| Review | [openstudio-mvp-review.md](openstudio-mvp-review.md) — 0 P0, 6 P1 and 2 P2 findings, all resolved with regression tests; merge recommendation **READY** |
| Rhino 8 validation | **PASSED (2026-07-19, user-performed)** — see the dedicated section below |
| Next milestone | **PR into `sow/2026-Q3`** |

## Rhino 8 real-model validation

User-performed, 2026-07-19, commit `fd2c0a7` (assemblies deployed to `%APPDATA%\SAM` by the
`-m:1` solution build). Independently verified against the generated artifacts in
`C:\Users\Virtual Machine\Documents\SAM_daily\2026-07-19 OpenStudio\simulation-fixed`:

| Check | Result |
| --- | --- |
| Rhino 8 startup | PASS |
| Grasshopper GHA loading | PASS |
| `SAMAnalytical.ToOpenStudio` appears and executes | PASS |
| `OpenStudio.RunModel` appears and executes | PASS |
| Real SAM AnalyticalModel converted | PASS |
| OSM generation (`000000_SAM_AnalyticalModel.osm`) | PASS |
| OSW generation (`000000_SAM_AnalyticalModel.osw`) | PASS |
| OpenStudio CLI execution | PASS — `finished.job`: "Finished Workflow"; `EnergyPlus Completed Successfully` |
| SQL produced and readable (`run\eplusout.sql`) | PASS |
| Fatal errors | **0** |
| Severe errors | **0** |
| Previous R-value fatal error (`R Value below lowest allowed value`) | **RESOLVED — absent** |
| Opaque gas cavities → `Material:AirGap` | PASS — `Ar90Up_Air_50mm_1.25W/m2K` → R = **0.8** m²K/W; `Ar90Up_Air_50mm_1.95W/m2K` → R = **0.512820512820513** m²K/W (R = 1/h) |
| `WindowMaterial:Gas` in opaque constructions | **0 occurrences** (none in the whole model) |
| Heating (verified from SQL) | **3758.36 kWh** (component displayed 3758.359753 kWh) |
| Cooling (verified from SQL) | **0.00 kWh — genuinely present** (8760 hourly rows summing to 0; London St. James's Park TMYx; not a missing result) |
| Conditioned-zone result count | 1 = conditioned-zone count (`SAM_IDEALLOADS_CELL_1_99C96B9E`) |
| Environment periods in SQL | single annual weather run — no design days |
| Native DLL / assembly-resolution errors | none |
| Remaining conversion warning | `SAM-OS-IC-001`: latent equipment gains are not converted in the MVP (documented limitation) |
| Remaining EnergyPlus warnings (non-blocking) | ground temperatures default 18 °C; floor/roof "upside down" auto-fixed by E+; zone volume −100 → 10 m³ (source-model geometry); meter key names; monthly tabular reports; LifeCycleCost resources; 3 unused schedules; EPW design-condition field count |

Zero cooling is a valid result here: the time series exists (8760 hourly values summing to
0.00 kWh), extraction completed successfully, heating is finite and > 0, and no fatal or severe
errors occurred.

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
| **Review fixes** | `4d28513` … `8c3367f` | 73/73 | Independent review ([openstudio-mvp-review.md](openstudio-mvp-review.md)): P1-01 weekly-profile calendar alignment; P1-02 leap-year/multi-day profile squash; P1-03 SpaceType same-name collision; P1-04 glazing front/back swap; P1-05 CLI timeout/deadlock; P2-01 self-intersecting polygons; P2-02 quote sanitization — one commit + regression tests each |
| **P1-06** | `fd2c0a7` | **82/82** | Real Rhino 8 smoke test (user): deployment passed, EnergyPlus failed fatally on opaque gas cavities (`OS:WindowMaterial:Gas` R = 0.000 in `InitConductionTransferFunctions`). Fix: explicit `OpenStudioMaterialUsage` — SAM `GasMaterial` in opaque `Construction` → `OpenStudio.AirGap` with R = 1/h from `GasMaterialParameter.HeatTransferCoefficient` [W/m²K] (validated, SAM-OS-MAT-001 on missing/invalid/below-minimum); in `ApertureConstruction` panes → `OpenStudio.Gas` unchanged; usage-aware cache keys; material-family validation; 9 new tests incl. opaque-air-gap E2E (exit 0, 0 fatal/severe, 4953.6/1555.5 kWh). **Real-model Rhino retest PASSED 2026-07-19** (see section above) |

## Known limitations at this point

- Pre-existing benign `MSB3277` System.Memory warning (see audit doc §6).
- `Create.Model` (pre-existing) loads OSM without `VersionTranslator` — fine while SDK == CLI (3.10.0 both).
- DDY design-day import not implemented (optional per plan; Ideal Loads annual runs need no sizing periods).
- Schedules are 365-day hourly `ScheduleFixedInterval`; profiles longer than 8760 hours (leap years) are truncated to the first 8760 hours with a warning (documented non-leap policy); day-composed weekly profiles align to the run calendar (EPW-derived or explicit option; Monday-first fallback for weather-free conversions).
- Latent equipment gains and humidification/dehumidification setpoints not converted (documented warnings).
- Heating-only / cooling-only conditioning unsupported (both setpoint profiles required) — SAM_LadybugTools parity.
- Ideal Loads uses EnergyPlus object defaults; no capacity/air-flow limits, economizer, heat recovery or humidity control.
- Opaque gas cavities become resistance-only `OS:Material:AirGap` layers (R = 1/h from SAM's cavity conductance); the convective/radiative split of the cavity is not modelled beyond that (standard EnergyPlus approach).
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

Then perform the user Rhino 8 retest: open Rhino 8 + Grasshopper (assemblies are already
deployed to `%APPDATA%\SAM` by the build), rerun the same analytical model as the 2026-07-19
smoke test, and confirm: both components load; OSM/OSW generated; CLI exit code 0; SQL present;
fatal = 0; severe = 0; heating/cooling results returned; no R-value error. Reference material:
audit doc [openstudio-mvp-audit.md](openstudio-mvp-audit.md); review doc
[openstudio-mvp-review.md](openstudio-mvp-review.md); LadybugTools semantic reference paths in
plan §1; binding surface can be inspected via reflection on
`%USERPROFILE%\.nuget\packages\openstudio\3.10.0\build\netstandard2.0\x64\OpenStudio.dll`.
