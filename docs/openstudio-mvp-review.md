# SAM → OpenStudio MVP — independent review

**Reviewer:** independent senior C# building-performance engineer (Stage 3/4 combined session)
**Date:** 2026-07-19
**Branch:** `feature/analytical-model-to-openstudio-mvp` (base `sow/2026-Q3` @ 94dfce9)
**Contract:** [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md)
**Method:** every claim in [openstudio-mvp-status.md](openstudio-mvp-status.md) treated as unverified
until reproduced. All ten commits inspected individually; every changed production and test file
read in full; SAM sources and the SAM_LadybugTools reference converters read for every unit,
orientation convention and parameter semantic; EnergyPlus I/O Reference used to settle glazing
front/back semantics.

---

## 1. Executive summary

The implementation is architecturally faithful to the plan: responsibilities are correctly
separated across `SAM.Core.OpenStudio` / `SAM.Geometry.OpenStudio` / `SAM.Analytical.OpenStudio` /
thin Grasshopper components; no Honeybee/Ladybug dependency was introduced; the source
`AnalyticalModel` is deep-copied before preprocessing and is not mutated; adjacency is driven by
SAM topology (never geometric matching); constructions are mirrored correctly on internal pairs;
SQL extraction was independently cross-checked and is exact.

Five P1 findings and two P2 findings were confirmed by reproduction in the original review pass
(each with executable evidence below). No P0 finding exists: nothing crashes the converter,
corrupts results or endangers the repository in the tested paths. **All seven findings are
resolved**, each in its own commit with a regression test.

A real Rhino 8 Grasshopper smoke test was then performed by the user: deployment succeeded
(GHA loads, both components appear and execute, OSM/OSW generated, CLI starts, no native-DLL or
assembly errors), but the real SAM model failed fatally in EnergyPlus on opaque gas cavities —
recorded and resolved as **P1-06**.

| ID | Priority | Finding | Status |
| --- | --- | --- | --- |
| P1-01 | P1 | Weekly (day-composed) profiles were tiled Monday-first with no alignment to the run calendar; with the pinned EPW (Jan 1 = Sunday) every weekday/weekend pattern landed on the wrong calendar day | Resolved `4d28513` |
| P1-02 | P1 | Profiles with more than 8760 values (leap year 8784) or multi-day flat sequences were silently block-averaged into a single "average day" — silent schedule corruption | Resolved `1971bdc` |
| P1-03 | P1 | `SpaceType` deduplication by sanitized name silently merged two internal conditions that share a name but differ in gains or profiles | Resolved `f46c98f` |
| P1-04 | P1 | Glazing front/back optical properties were swapped relative to EnergyPlus semantics (SAM `Internal*` written to the E+ *front* = exterior-facing side) | Resolved `fea508f` |
| P1-05 | P1 | CLI runner: `TimeoutSeconds` never fired while the process ran (proven: 120.6 s block with a 5 s timeout); sequential `ReadToEnd()` on both pipes could deadlock; process tree not killed | Resolved `20b2b85` |
| P1-06 | P1 | SAM `GasMaterial` in opaque constructions was converted to `OS:WindowMaterial:Gas`, which reports R = 0.000 to EnergyPlus `InitConductionTransferFunctions` → fatal (the real Rhino smoke-test model) | Resolved (this branch tip) |
| P2-01 | P2 | Self-intersecting polygons with non-zero signed area were accepted silently and became invalid EnergyPlus surfaces | Resolved `add236f` |
| P2-02 | P2 | `SanitizeName` kept `'` and `"`; such names broke the SQL extraction query and the zone's results were silently dropped | Resolved `8c3367f` |

Environment note: the prebuilt SAM/SAM_SQLite assemblies under `SAM\build` and
`SAM_SQLite\build` were missing on this machine (only third-party DLLs present). Per the task
brief they were rebuilt **without modifying sibling source**:
`dotnet build ..\SAM\SAM.sln -c Debug` and `dotnet build ..\SAM_SQLite\SAM_SQLite.sln -c Debug`
(default platform; both completed with 0 errors; sibling git trees remained clean before and
after). The same wipe recurred twice more during the session (an unrelated concurrent process on
this shared machine — see §10) and was repaired the same way each time. No sibling source file
was changed, committed or otherwise altered.

## 2. Baseline reproduced

| Item | Claimed | Reproduced |
| --- | --- | --- |
| Build | 0 errors | `dotnet build SAM_OpenStudio.sln -c Debug -p:Platform=x64` — **0 errors**, only the pre-existing benign MSB3277 `System.Memory` warning (audit §6) |
| Tests | 56/56 | `dotnet test tests/SAM.Analytical.OpenStudio.Tests -c Debug -p:Platform=x64` — **56/56 passed, 0 skipped** |
| SDK | OpenStudio NuGet 3.10.0 | Confirmed in all three library csproj files |
| CLI | 3.10.0+86d7e215a1 | `openstudio openstudio_version` → `3.10.0+86d7e215a1` at `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` |
| E2E runs | two complete simulations | Three independent runs executed outside the test suite (temp driver), all exit 0, 0 fatal, 0 severe |
| SQL extraction | DISTINCT KeyValue sums, kWh | Cross-checked with an independent SQLite query (below) — exact match |
| Source mutation | none | M7 test passes; copy-constructor chain inspected (`AnalyticalModel` → `AdjacencyCluster` → panels) |
| Honeybee runtime | none | No Honeybee/Ladybug reference in any changed project |
| Committed artifacts | none | `git ls-files` — no binaries/run directories; only the intentionally pinned weather files |
| SPDX headers | all new files | All 58 changed `.cs` files carry the header (`Test.cs` appears in the diff only because it was deleted) |

Independent three-simulation baseline (driver: temp console app calling the public API with the
repository fixtures; paths contain spaces — quoting therefore exercised):

| Fixture | OSM | exit | fatal | severe | zones w/ results | heating kWh | cooling kWh | duration |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SingleBox (one conditioned zone) | `runs\single_box\Single_Box_Model.osm` | 0 | 0 | 0 | 1/1 | **2621.7** | **1745.0** | 3.9 s |
| TwoAdjacentBoxes (two zones, internal wall) | `runs\two_adjacent\Two_Box_Model.osm` | 0 | 0 | 0 | 2/2 | **4875.0** | **2830.8** | 2.8 s |
| TwoStackedBoxes (stacked, shared floor) | `runs\two_stacked\Two_Stacked_Box_Model.osm` | 0 | 0 | 0 | 2/2 | **1538.2** | **2563.6** | 2.6 s |

SQL cross-check (independent Python/sqlite3 query on `two_adjacent\run\eplusout.sql`):

```text
EnvironmentPeriods: (1, 'SAM_RUNPERIOD_ANNUAL', type=3 weather-run)   <- single period, no design days
RDD: one Hourly row per zone per variable, units J
SUM(ReportData) per zone: H 2403.28 / 2471.67 kWh, C 1737.41 / 1093.42 kWh — identical to the runner output
8760 ReportData rows per zone/variable; J -> kWh / 3.6e6 verified
```

The runner's per-`KeyValue` `SUM` is therefore correct for the MVP configuration (no sizing
periods → single environment period → no double counting; `DISTINCT KeyValue` cannot discard
valid data because each zone/variable has exactly one dictionary row).

OSM artifact checks (two_adjacent): `OS:WeatherFile … Start Day of Week = Sunday` (from the
EPW `DATA PERIODS` line); Forward/Reverse constructions carry mirrored material handles
(Brick/Insulation/Plasterboard ↔ Plasterboard/Insulation/Brick); pane construction Glass/Air/Glass.

---

## 3. P0 findings

None.

---

## 4. P1 findings

### P1-01 — Weekly-profile day-of-week misalignment

- **Priority:** P1
- **Summary:** Day-composed (weekly) SAM profiles were expanded assuming day 0 (1 Jan) is the
  first sub-profile ("Monday-first", LadybugTools `ScheduleRuleset` parity). The run calendar's
  actual start weekday comes from the EPW (Boston TMYx: **Sunday**) and was never consulted, so
  every weekly pattern was shifted by up to six days relative to the weather calendar.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/Profile.cs`
  (`AnnualHourlyValues`); `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/AnalyticalModel.cs`
  (full-pipeline overload never derived the start weekday).
- **Technical explanation:** SAM weekly profiles are 168-hour cycles whose de-facto convention
  (via SAM_LadybugTools `ScheduleRuleset.cs:73-96`) maps sub-profile 0 → Monday … 6 → Sunday, and
  EnergyPlus aligns that ruleset to real calendar weekdays from the weather file. The MVP baked
  the pattern into a `ScheduleFixedInterval` starting 1 Jan with `dailyValues[dayIndex % 7]` —
  correct only when 1 Jan is a Monday.
- **Reproduction evidence:** weekly profile Mon=1/Sun=0 converted and inspected:
  `day0 (1 Jan, a Sunday per the EPW) = 1` (should be 0), `day7 (Sunday) = 1` (should be 0);
  `day1` and `day6` happened to match. Generated OSM shows `OS:WeatherFile, Start Day of Week = Sunday`.
  Fixture profiles are all day-uniform, so no existing test could see this.
- **Impact on generated OSM / results:** for any real SAM model with weekday/weekend profiles,
  occupancy, lighting, equipment, infiltration and setpoint patterns landed on wrong calendar
  days (weekend gains on weekdays and vice versa). Annual totals were preserved; daily/hourly
  loads and peak timing were wrong.
- **Recommended correction:** derive the run start weekday from the EPW
  (`EpwFile.startDayOfWeek().value()`, Sunday=0) in the full-pipeline overload, expose an explicit
  override on `OpenStudioConversionOptions`, carry a Monday=0 offset on the conversion context and
  index `dailyValues[(dayIndex + offset) % 7]`; default offset 0 (Monday) keeps the geometry-only
  overload backward compatible.
- **Required regression test:** weekly profile where Monday ≠ weekend values, converted through the
  EPW pipeline (Jan 1 = Sunday): schedule day 0 must equal the weekend value, day 1 the Monday
  value; plus an override-path test.
- **Status:** **Resolved** — commit `4d28513`.

### P1-02 — >8760-value and multi-day flat profiles silently squashed

- **Priority:** P1
- **Summary:** `DayHourlyValues` block-averaged any flat profile whose value count is a multiple
  of 24 into 24 hourly values. An 8784-hour leap-year profile (block 366) or a 168-hour weekly
  sequence (block 7) became one synthetic "average day" tiled 365 times — with **no diagnostic**.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/Profile.cs:168-279`.
- **Technical explanation:** SAM's own annual-expansion semantics are cyclic tiling at the
  profile's own period (`Profile.GetYearlyValues()` → `GetValues(Range(0,8759))` with
  `BoundedIndex` wrapping; identical to the `profile[i]` indexer, SAM `Profile.cs:1207-1237`).
  The MVP's documented rules 3–5 (hold / block-average / tile-with-warning) diverged from that
  and, for any count > 24 that is a multiple of 24, destroyed the sequence silently. The plan
  (§10) requires "explicitly handle leap years; validate expected value counts".
- **Reproduction evidence:** 8784-value profile with an 08–18 daily pattern converted: schedule
  `SAM_Schedule_Leap_Occupancy_*` contained `hour0 = 0.4126` — an average of 366 source values —
  instead of the real pattern; zero diagnostics raised.
- **Impact:** leap-year or multi-day profiles lost their entire temporal pattern; gains and
  setpoints became flat average days. Silent.
- **Recommended correction:** replace rules 3–5 with SAM-native semantics: 8760 → as-is;
  >8760 → first 8760 hours with a `SAM-OS-SCH-001` **warning** (documented non-leap policy);
  otherwise tile cyclically through the SAM indexer (`profile[i]`, which already wraps within
  Min..Max) — identical to `GetYearlyValues()`. Day sub-profiles expand per hour with the same
  indexer. Update `docs/SAM_OPENSTUDIO_INTERNAL_CONDITION_MAPPING.md`.
- **Required regression test:** 8784-value profile → schedule hours 0..8759 equal the source's
  first 8760 values, warning raised; 168-value weekly sequence preserved hour-for-hour (not
  averaged); 24-value daily unchanged; gappy profile → error.
- **Status:** **Resolved** — commit `1971bdc`.

### P1-03 — Same-named internal conditions merged into one SpaceType

- **Priority:** P1
- **Summary:** `SpaceType` deduplication keyed on the sanitized internal-condition **name only**.
  Two conditions sharing a name but differing in parameters or profile references silently shared
  the first condition's loads.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/InternalCondition.cs:30-45`.
- **Technical explanation:** SAM's `Space.InternalCondition` setter clones with a new Guid
  (`SAM.Analytical/Classes/Space.cs:94`), so Guid-keying cannot dedup true clones — but
  SAM_LadybugTools dedups per-Guid (`Model.cs:243-258`; `UniqueName` includes `guid8`), i.e. the
  reference implementation **never merges** distinct objects. The mapping doc's "same semantics as
  SAM_LadybugTools" claim was inaccurate. Name-only keying merged genuinely different conditions
  (same library name redefined in another library; same name re-parameterised; names that
  sanitize identically).
- **Reproduction evidence:** two-box fixture; Space B given a condition also named `Office` but
  with `LightingGainPerArea = 99` (Space A: 8). Result: **one** `SpaceType`
  (`SAM_InternalCondition_Office`), both spaces referencing it — Space B silently simulated at
  8 W/m² instead of 99 W/m².
- **Impact:** wrong loads for every space whose condition collided by name; silent.
- **Recommended correction:** keep name-based readability but qualify the key with a deterministic
  content hash: serialise the condition via `ToJsonObject()`, recursively drop `Guid` properties
  (clone-safe), hash (MD5, first 8 hex) and form `SAM_InternalCondition_<SanitizedName>_<hash8>`.
  Identical clones still dedup (proven: clone → identical JSON); different content splits
  (proven: gains change → different JSON). Over-splitting on formatting noise is the safe
  direction (correct results, merely not shared).
- **Required regression test:** same name / different gains → two SpaceTypes with correct per-space
  W/m²; same name / identical content → one SpaceType; determinism across conversions.
- **Status:** **Resolved** — commit `f46c98f`.

### P1-04 — Glazing front/back optical properties swapped

- **Priority:** P1
- **Summary:** SAM `InternalSolarReflectance` / `InternalLightReflectance` / `InternalEmissivity`
  were written to the EnergyPlus **Front** side and the `External*` values to the **Back** side.
  EnergyPlus defines Front as "the side of the layer opposite the zone" (exterior-facing for
  exterior windows) — i.e. SAM `External*` must map to Front and `Internal*` to Back.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/Material.cs`;
  `docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md` (glazing table).
- **Technical explanation:** EnergyPlus I/O Reference, *Materials for Glass Windows and Doors*:
  "'Front side' is the side of the layer opposite the zone in which the window is defined… for
  exterior windows, 'front side' is the side closest to the outdoors." SAM parameter descriptions
  are explicit ("External Emissivity", "Internal Solar Reflectance"). The implementation mirrored
  `SAM_LadybugTools/EnergyWindowMaterialGlazing.cs:19-26`, which carries the same inversion; the
  mapping doc flagged exactly this assignment for independent review. Symmetric glass (including
  the fixture's 0.07/0.07, 0.84/0.84) is unaffected, which is why no test caught it.
- **Reproduction evidence:** code inspection + E+ documentation (quoted above); the generated OSM
  showed identical front/back values only because the fixture glass is symmetric.
- **Impact:** for coated or otherwise asymmetric glazing (the norm for real low-e products), solar
  reflectance and IR emissivity were applied to the wrong pane faces: wrong gap heat transfer,
  wrong solar heat gain, wrong U-value behaviour. Material simulation risk — P1 per the rubric.
- **Recommended correction:** swap the six assignments (Front ← `External*`, Back ← `Internal*`),
  document the deliberate deviation from SAM_LadybugTools (with the E+ citation) in the mapping
  doc.
- **Required regression test:** asymmetric glass (internal emissivity 0.05 / external 0.84;
  internal reflectance 0.11–0.12 / external 0.31–0.32) → front/back assignments verified for all
  six fields.
- **Status:** **Resolved** — commit `fea508f`.

### P1-05 — CLI timeout ineffective; pipe-read deadlock risk; no process-tree kill

- **Priority:** P1
- **Summary:** `ExecuteCli` read `StandardOutput.ReadToEnd()` then `StandardError.ReadToEnd()`
  and only then called `WaitForExit(timeout)`. While the child ran, `ReadToEnd()` blocked: the
  timeout was never evaluated, and if the child filled the stderr pipe buffer while stdout stayed
  open, parent and child deadlocked permanently.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Classes/OpenStudioSimulationRunner.cs:251-286`.
- **Technical explanation:** the classic redirected-pipes pattern requires asynchronous reads (or
  a reader thread per stream) **before** waiting with a timeout. Here the timeout only applied
  after both streams reached EOF — i.e. after the process had already exited. The plan (§11)
  requires "CLI process execution with timeout and cancellation". On timeout only the root
  process could be killed; the CLI's EnergyPlus child would be orphaned (Windows has no managed
  tree-kill; a Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` is the correct mechanism).
- **Reproduction evidence:** fake `openstudio.exe` (sleeps 120 s, writes 100 bytes) run through
  the public API with `TimeoutSeconds = 5`: the call returned after **120.6 s** with
  `ExitCode = 0` — the timeout never fired.
- **Impact:** a hung CLI froze the caller (including the Grasshopper UI thread) indefinitely;
  under output-heavy failure modes the run could deadlock even without a hang. In Rhino this is
  a UI-blocking failure exactly where the components are used.
- **Recommended correction:** `BeginOutputReadLine`/`BeginErrorReadLine` into StringBuilders;
  `WaitForExit(timeoutMs)`; on timeout terminate via a best-effort Job Object (fallback
  `Kill()`), drain with a final parameterless `WaitForExit()`, return exit −1 and a `TIMEOUT`
  diagnostic; err-file parsing best-effort after a kill (file can be locked/partial).
- **Required regression test:** real CLI, one-zone fixture, `TimeoutSeconds = 1` → run fails with
  a timeout diagnostic and exit −1 in bounded time, and no `energyplus`/`openstudio` process
  survives (bounded poll).
- **Status:** **Resolved** — commit `20b2b85`. Manual re-verification: the same 120 s sleeper now
  dies at 5.7 s with exit −1.

### P1-06 — Opaque gas cavities became WindowMaterial:Gas (fatal in EnergyPlus)

- **Priority:** P1
- **Summary:** every SAM `GasMaterial` was converted to `OpenStudio.Gas`
  (`OS:WindowMaterial:Gas`) regardless of context, and the construction converter used the same
  material path for opaque `Construction` layers and `ApertureConstruction` pane layers.
  `OS:WindowMaterial:Gas` is only valid in fenestration constructions; inside an opaque
  construction EnergyPlus computes R = 0.000 for it and aborts.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/Material.cs`
  (single gas path); `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/Construction.cs`
  (usage-blind material resolution).
- **Technical explanation:** SAM models an opaque air/gas cavity as a *resistive* layer:
  `GasMaterialParameter.HeatTransferCoefficient` holds the cavity conductance h [W/m²K]
  (SAM `Query.HeatTransferCoefficient`: "Heat Transfer Coefficient (Thermal Conductance)
  [W/m2K]"; written by `Query.UpdateHeatTransferCoefficients` from gas type, thickness and
  tilt via `AirspaceConvectiveHeatTransferCoefficient` for opaque air cavities), and
  `Query.AirspaceThermalResistance` defines the opaque-construction airspace model per
  BS EN ISO 6946:2017. EnergyPlus' opaque-cavity equivalent is `OS:Material:AirGap`
  (a resistance-only layer, R [m²K/W]); the fenestration equivalent is
  `OS:WindowMaterial:Gas` (gas type + thickness, valid only between glazing panes).
- **Reproduction evidence (real Rhino 8 smoke test, 2026-07-19):** deployment succeeded (GHA
  loads; `SAMAnalytical.ToOpenStudio` and `OpenStudio.RunModel` appear and execute; OSM/OSW
  created; CLI started; no native-DLL or assembly errors). EnergyPlus then failed fatally
  (`SAM_daily\2026-07-19 OpenStudio\simulation\run\eplusout.err`):
  `** Severe ** InitConductionTransferFunctions: Material=SAM_GASMATERIAL_AR90UP_AIR_50MM_1.25W/M2K_50MM_8484B96E R Value below lowest allowed value … Lowest allowed value=[1.000E-003], Material R Value=[0.000]`
  (twice, wall `SIM_EXT_SLD`), and the same for `…_1.95W/M2K_…` (roof `SIM_EXT_SLD_Roof`),
  `** Fatal ** Program terminated for reasons listed (InitConductionTransferFunctions)`.
  The generated OSM/IDF show `OS:WindowMaterial:Gas, Air, 0.05 m` as layers of the opaque
  constructions `SIM_EXT_SLD_Forward` (layers 2 and 5) and `SIM_EXT_SLD_Roof_Forward` (layer 4).
- **Impact:** any real SAM model with opaque gas cavities (a standard SAM construction pattern)
  produced an un-runnable EnergyPlus model. Material simulation risk — P1.
- **Recommended correction:** explicit `OpenStudioMaterialUsage` context: opaque →
  `OpenStudio.AirGap` with **R = 1/h** from `GasMaterialParameter.HeatTransferCoefficient`
  (validated: present, finite, > 0, R ≥ 0.001 m²K/W, else SAM-OS-MAT-001 before CLI);
  fenestration → `OpenStudio.Gas` unchanged. Material cache keys include the usage
  (`<Guid>:OpaqueAirGap:<R>` / `<Guid>:WindowGas:<thickness>`); construction-level family
  validation rejects `Gas` in opaque sets, `AirGap` in pane sets and mixed families; no silent
  layer omission.
- **Required regression tests:** cavity becomes AirGap; h = 1.25 → R = 0.8 m²K/W; h = 1.95 →
  R ≈ 0.5128205 m²K/W; pane gas stays Gas (type + thickness); same GasMaterial in both contexts
  → two distinct objects/cache entries; missing conductance → SAM-OS-MAT-001; R below the E+
  minimum → SAM-OS-MAT-001; glazing layer in an opaque construction rejected; end-to-end
  EnergyPlus run of an opaque-air-gap fixture (exit 0, SQL present, no fatal/severe, finite
  loads).
- **Status:** **Resolved** — the commit introducing this change (see §9).

---

## 5. P2 findings

### P2-01 — Self-intersecting polygons accepted silently

- **Priority:** P2
- **Summary:** `ValidatePolygon` checked duplicates, collinearity, planarity and minimum area,
  but never self-intersection. A planar self-intersecting polygon with non-zero signed area
  (e.g. a star pentagon) passed without diagnostics; a bowtie was caught only accidentally (its
  signed area is ~0).
- **Affected files:** `SAM_OpenStudio/SAM.Geometry.OpenStudio/Query/ValidatePolygon.cs:24-61`.
- **Reproduction evidence:** star polygon (0,0)(10,0)(3,8)(5,-4)(7,8), signed area 48 m² →
  validation returned **no diagnostics**; bowtie → `SAM-OS-GEO-001` via the degenerate-normal
  path.
- **Impact:** invalid EnergyPlus surfaces (wrong area/shadowing or E+ geometry errors downstream)
  from geometry the converter claimed to have validated.
- **Recommended correction:** proper-crossing segment-intersection test in the polygon plane
  (O(n²), ignore shared endpoints, tolerance-aware touch detection) → `SAM-OS-GEO-001` error.
- **Required regression test:** star and bowtie rejected with `SAM-OS-GEO-001`; concave L-shape
  and the full fixture set unaffected.
- **Status:** **Resolved** — commit `add236f`.

### P2-02 — Apostrophes/quotes survive name sanitization and break SQL extraction

- **Priority:** P2
- **Summary:** `SanitizeName` stripped `,` `;` `!` whitespace/control chars but kept `'` and `"`.
  Those characters flowed into zone/system names and then into the SQL
  `WHERE rdd.KeyValue = '…'` literal, breaking the query; the zone was then silently omitted from
  the load summary (`execAndReturnFirstDouble` fails → `continue`).
- **Affected files:** `SAM_OpenStudio/SAM.Core.OpenStudio/Query/SanitizeName.cs:29`;
  `SAM_OpenStudio/SAM.Analytical.OpenStudio/Classes/OpenStudioSimulationRunner.cs:314-335`.
- **Reproduction evidence:** code inspection (sanitizer set; SQL string built with raw `'`
  quoting).
- **Impact:** zones with apostrophes in SAM space names lost their results silently (result count
  < conditioned-zone count with no diagnostic).
- **Recommended correction:** add `'` and `"` to the sanitized set (underscore-collapsed like the
  rest). The SQL query stays simple and the deterministic-name contract is unchanged.
- **Required regression test:** `SanitizeName("O'Brien \"Annex\"")` contains neither quote
  character; a space named `O'Brien` produces a valid deterministic name.
- **Status:** **Resolved** — commit `8c3367f`.

---

## 6. P3 observations (recorded, not fixed in this round)

| # | Observation | Location |
| --- | --- | --- |
| P3-01 | `IsConditioned` uses culture-sensitive `ToLower()` (Turkish-ı edge); LadybugTools has the same pattern — move to `ToLowerInvariant()` later | `Query/IsConditioned.cs:31` |
| P3-02 | SpaceType-level load densities and infiltration are evaluated for the first space of a shared condition (documented approximation; per-area bases scale correctly for identical conditions) | `Convert/ToOpenStudio/InternalCondition.cs` |
| P3-03 | Internal-pair side assignment (forward → first-created side) is the mirror opposite of LadybugTools' side choice; both are valid E+ mirrors and SAM defines no partition side semantics | `Convert/ToOpenStudio/AnalyticalModel.cs:341-356` vs `Face.cs:40-48` reference |
| P3-04 | `ScheduleMap` is keyed by profile Guid only; the same profile reused under a different `ProfileType` would keep the first type limits/name | `Convert/ToOpenStudio/Profile.cs:31-34` |
| P3-05 | No story fallback when a model has no floor-group panels (LadybugTools falls back to walls); spaces simply get no `BuildingStory` | `Convert/ToOpenStudio/BuildingStory.cs:41-46` |
| P3-06 | No `CancellationToken`; a running CLI blocks the Grasshopper UI thread (documented) | `Classes/OpenStudioSimulationRunner.cs` |
| P3-07 | `OpenStudioConversionResult.Model` ownership/disposal not documented (callers must dispose) | `Classes/OpenStudioConversionResult.cs` |
| P3-08 | Full pipeline still invokes the CLI when conversion already has errors (result stays `IsValid=false`; diagnostics are doubled but correct) | `Convert/ToOpenStudio/AnalyticalModel.cs:51-53` |
| P3-09 | EPW ground temperatures are not imported (E+ warns, uses 18 °C) — standard OpenStudio behaviour, acceptable for Ideal Loads MVP | `Convert/ToOpenStudio/Weather.cs` |
| P3-10 | `Create.Model` (pre-existing) loads OSM without `VersionTranslator` — fine while SDK == CLI | `SAM.Core.OpenStudio/Create/Model.cs` |
| P3-11 | DDY design-day import not implemented — optional per plan; annual Ideal Loads needs no sizing periods | runner |
| P3-12 | Conditioned space with zero convertible panels still gets a thermostat + Ideal Loads on an empty zone (E+ may complain); degenerate-input edge | `Convert/ToOpenStudio/AnalyticalModel.cs:418-447` |
| P3-13 | `RunPeriod` "Number of Times Runperiod to be Repeated" and north-rotation left at defaults (documented) | `Convert/ToOpenStudio/SimulationSettings.cs` |

## 7. Areas reviewed with no defect found

- **Architecture/repository (§5.1):** clean layer separation; Grasshopper components thin (no
  conversion logic); no Honeybee runtime; sibling repos untouched; no committed artifacts; SPDX
  headers on all new files; coherent documented public API.
- **Source-model integrity (§5.2):** deep copy before `OffsetAperturesOnEdge` /
  `ReplaceTransparentPanels` (SAM copy constructors verified); no-mutation and deterministic-name
  tests pass; repeated conversion with disposal stable.
- **Geometry (§5.3):** metres/global coordinates retained; Newell normal correct; duplicate/
  closing/collinear removal verified by tests; planarity + min-area rejection verified; floor/roof/
  wall classification incl. the stacked-space flip matches `Face.cs:55-58`; story assignment by
  nearest elevation; floor area and volume exact (20 m² / 60 m³). Only the self-intersection gap
  (P2-01) and a tight absolute planarity tolerance (1e-6 m default; configurable) noted.
- **Adjacency (§5.4):** internal panels produce exactly two surfaces with opposite outward
  normals (test asserts dot = −1); `setAdjacentSurface` reciprocal (tested); broken internal
  topology reported (`SAM-OS-ADJ-001`) and set Adiabatic — reproduced; internal aperture pairing
  (door on shared wall) reproduced — reciprocal `setAdjacentSubSurface`; shading panels go to a
  building-level `ShadingSurfaceGroup`, never heat-transfer surfaces.
- **Materials/constructions (§5.5):** units verified against SAM source (k W/m·K, ρ kg/m³,
  c J/kg·K, thickness m from `ConstructionLayer.Thickness` with `DefaultThickness` fallback +
  warning); absorptance = 1 − reflectance; gas types restricted with errors; layer order proven
  outside-first on external walls **and** mirrored on both internal sides, in tests **and** in the
  generated OSM; caches keyed by (Guid, thickness) and (Guid, direction) — no wrong reuse found.
  Only the glazing front/back swap (P1-04).
- **Internal conditions (§5.7):** every mapped row in the IC mapping doc traced to SAM query
  sources (people/m², W/person activity, sensible/(sensible+latent), W/m² lights/equipment,
  ACH×volume/3600 infiltration over sun-exposed area — `InfiltrationAirFlowPerExteriorArea` parity
  confirmed line-by-line; `OutsideSupplyAirFlow` m³/s absolute per space). Only the dedup key
  (P1-03).
- **Thermostats/Ideal Loads (§5.8):** central `IsConditioned` matches the LadybugTools name
  convention (unconditioned/external); both-setpoints requirement matches LadybugTools
  `ProgramType.cs:184` (documented limitation, not a defect); heating ≤ cooling validated hourly;
  conditioned zones get exactly one thermostat + one Ideal Loads; unconditioned get neither
  (tested).
- **Weather/settings (§5.9):** EPW validation, site population, run period, 6 timesteps/h, sizing
  disabled, six hourly output variables — all verified in code and in the generated OSM.
- **Runner (§5.10 beyond P1-05):** discovery order correct (explicit → PATH → direct installs →
  ladybug_tools); explicit-path precedence correct; quoting handles the space-containing repo path
  (all reproduction paths contain spaces); sequential runs into the same directory are clean (the
  CLI recreates `run/`; run-after-failure does not pick up stale results — reproduced); OSM-only
  vs OSW execution correct; missing CLI and invalid/malformed EPW produce structured
  `SAM-OS-RUN-001` diagnostics — reproduced.
- **Error parsing (§5.11):** `** Severe` / `** Fatal` detection correct; warnings not escalated;
  first lines retained; stale `.err` cannot be confused (success requires exit 0 + SQL).
- **SQL extraction (§5.12):** independently cross-checked (§2 above) — correct environment period,
  variable names valid for the bundled EnergyPlus 25.1, hourly frequency, J→kWh, no
  double-counting, missing-zone rows distinguishable from zero.
- **Diagnostics (§5.13):** all silent-substitution searches (`catch`, `return null`, `continue`,
  `AlwaysOn`, `new Construction`, `new Material`) land either on diagnosed paths or on
  documented LadybugTools-parity defaults (0.3 radiant fraction etc.); error severity drives
  `IsValid` correctly (proven by the degenerate-panel and missing-profile tests).
- **Grasshopper (§5.14):** components are thin, `_run`-gated, exceptions become runtime messages;
  no OpenStudio native objects cross the boundary (paths/diagnostics only). Rhino 8 runtime
  compatibility is **not** claimed — human smoke test remains mandatory.
- **Tests (§5.15):** assertions are semantic (values, layer sequences, counts, normals), negative
  paths covered (missing material, missing profile, inverted setpoints, degenerate panel),
  simulation tests fail (not skip) when CLI/EPW is absent, output directories are deleted before
  use. Gaps that became findings: weekly-profile day alignment, leap-year profiles, same-name IC
  collision, asymmetric glazing, CLI timeout, self-intersection, quote sanitization.

## 8. Test and simulation evidence index

| Evidence | Where |
| --- | --- |
| Baseline build/test (56/56) | §2; Stage-D rerun in §10 |
| Three independent simulations | §2 table (temp driver, not the test suite) |
| SQL cross-check | §2 (Python/sqlite3, exact match) |
| Weekly misalignment | §4 P1-01 (probe: Mon=1/Sun=0) |
| Leap-year squash | §4 P1-02 (probe: 8784-value profile) |
| Same-name IC merge | §4 P1-03 (probe: 99 vs 8 W/m²) |
| Glazing front/back | §4 P1-04 (E+ I/O Reference quote) |
| CLI timeout | §4 P1-05 (fake CLI, 120.6 s vs 5 s; post-fix 5.7 s) |
| Self-intersection | §5 P2-01 (star polygon accepted) |
| Broken adjacency / internal aperture / sequential runs / missing CLI / invalid EPW | §7 (probes) |

## 9. Corrections implemented (Stage C)

| Commit | Finding | Regression test(s) |
| --- | --- | --- |
| `4d28513` fix: align weekly profile day-of-week with the run calendar (review P1-01) | P1-01 | `WeeklyProfile_AlignsWithWeatherFileStartDay`, `WeeklyProfile_MondayFirst_WithoutWeatherFile`, `WeeklyProfile_FirstDayOfWeekOption_OverridesDefault` |
| `1971bdc` fix: stop silently squashing leap-year and multi-day profiles (review P1-02) | P1-02 | `LeapYearProfile_IsTruncatedWithWarning_NotSquashed`, `WeeklyFlatProfile_IsTiledHourForHour_NotAveraged`, `DailyProfile_TilesUnchanged`, `ProfileWithGaps_RaisesError_NeverSilentNaN` |
| `f46c98f` fix: deduplicate SpaceTypes by name plus content hash (review P1-03) | P1-03 | `SameNamedConditions_WithDifferentContent_GetDistinctSpaceTypes`, `SameNamedConditions_WithDifferentProfiles_GetDistinctSpaceTypes`, `IdenticalClones_StillShareOneSpaceType` |
| `fea508f` fix: map glazing optical sides to EnergyPlus front/back correctly (review P1-04) | P1-04 | `AsymmetricGlazing_MapsExternalToFront_InternalToBack` |
| `20b2b85` fix: make the CLI timeout real and deadlock-safe (review P1-05) | P1-05 | `CliTimeout_KillsProcess_AndReportsTimeout` (plus the pre-existing healthy-run E2E tests); manual re-verification: 120 s sleeper now dies at 5.7 s with exit −1 (was 120.6 s, exit 0) |
| `add236f` fix: reject self-intersecting polygons (review P2-01) | P2-01 | `SelfIntersectingPolygon_IsAnError`, `BowtiePolygon_IsAnError`, `ConcavePolygon_IsAccepted` |
| `8c3367f` fix: sanitize quotes out of OpenStudio names (review P2-02) | P2-02 | `SanitizeName_RemovesQuotes`, `SpaceName_WithApostrophe_ConvertsWithDeterministicName` |
| (this branch tip) fix: distinguish opaque air gaps from window gas layers (review P1-06) | P1-06 | `OpaqueCavity_BecomesAirGap`, `OpaqueCavity_Resistance_FromConductance_1_25`, `OpaqueCavity_Resistance_FromConductance_1_95`, `WindowGas_RemainsGas_InAperturePane`, `SameGasMaterial_TwoContexts_SeparateObjects`, `MissingConductance_RaisesError_NeverSilent`, `ResistanceBelowEnergyPlusMinimum_RaisesError`, `GlazingLayer_InOpaqueConstruction_IsRejected`, `AirGapConstruction_EndToEnd_EnergyPlusRun_MeetsResultGate` |

Every fix was reproduced with a failing test first, corrected minimally, verified with the
focused test and then the complete suite. No existing milestone commit was rewritten.

## 10. Final validation (Stage D)

### Build and tests

| Gate | Result |
| --- | --- |
| `dotnet build SAM_OpenStudio.sln -c Debug -p:Platform=x64` | **Build succeeded** — 0 errors in every project (only the pre-existing benign MSB3277 `System.Memory` warning). See the environmental note below for the `%APPDATA%` deploy-copy caveat that applied on this shared machine |
| `dotnet test tests/SAM.Analytical.OpenStudio.Tests -c Debug -p:Platform=x64` | **82/82 passed, 0 skipped** (baseline 56 + 17 review regression tests + 9 P1-06 tests, including the opaque-air-gap end-to-end run) |

**Environmental note (not an MVP defect):** during this session a second, unrelated process on
this shared machine rebuilt the entire SAM suite and started a Rhino 8 instance (pid 5412,
started 11:37) with the SAM GHAs loaded from `%APPDATA%\SAM`. It twice wiped the prebuilt
`SAM\build` / `SAM_SQLite\build` assemblies (rebuilt by this session each time, per the brief —
no sibling source touched) and then locked the `.gha` files in `%APPDATA%\SAM`, so the
**pre-existing** post-build deploy copy (`build\*.dll → %APPDATA%\SAM`, unchanged from the base
branch and out of MVP scope) fails with `MSB3073` while Rhino holds those files. All projects
**compile** — the GH assemblies' DLLs are produced; only the convenience deploy copy collides
with the running Rhino. The full solution build was green at baseline (10:30) and after every
fix commit until the Rhino session started. A clean machine (or this machine with Rhino closed)
builds the solution end-to-end; this is exactly the class of machine-state the mandatory human
Rhino smoke test will exercise deliberately.

### Three-simulation validation (post-fix, final build)

| Fixture | OSM | exit | fatal | severe | zones w/ results | heating kWh | cooling kWh | duration |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SingleBox (one conditioned zone) | `final-runs\single_box\Single_Box_Model.osm` | 0 | 0 | 0 | 1/1 | **2621.7** | **1745.0** | 3.1 s |
| TwoAdjacentBoxes (two zones, internal wall) | `final-runs\two_adjacent\Two_Box_Model.osm` | 0 | 0 | 0 | 2/2 | **4875.0** | **2830.8** | 2.9 s |
| TwoStackedBoxes (stacked, shared floor) | `final-runs\two_stacked\Two_Stacked_Box_Model.osm` | 0 | 0 | 0 | 2/2 | **1538.2** | **2563.6** | 2.8 s |
| OpaqueAirGapBox (P1-06 regression: 50 mm cavity, h = 1.25 W/m²K → R = 0.8 m²K/W) | `e2e_airgap_box\Opaque_AirGap_Box_Model.osm` | 0 | 0 | 0 | 1/1 | **4953.6** | **1555.5** | ~3 s |

The first three loads are identical to the pre-fix baseline — expected: the fixes affect
weekly-profile calendar alignment, leap-year profiles, same-name condition merging, asymmetric
glazing, CLI failure handling, self-intersecting geometry, quoted names and opaque gas cavities
— of which only the last touches these fixtures' physics, and they contain no gas cavities.

### Stage-D checklist

| Item | Verified via |
| --- | --- |
| Exit code 0, no fatal, no severe — all three runs | table above |
| Zone result count == conditioned-zone count | 1/1, 2/2, 2/2 |
| Heating and cooling finite; at least one fixture with both > 0 | all three fixtures both > 0 |
| Floor area and volume preserved (20 m² / 60 m³, 1e-6) | M3 suite tests |
| Internal adjacency valid (reciprocal pairing, opposite normals) | M3 suite tests + clean two-zone run |
| Construction layer order (outside-first; mirrored internal sides) | M4 suite tests + OSM inspection |
| Weekly schedule semantics tested | 3 new weekly tests (EPW-derived, default, override) |
| Source `AnalyticalModel` unchanged | M7 no-mutation test (passes post-fix) |
| No temporary processes remain | timeout test asserts no orphaned `openstudio`/`energyplus`; none found |
| Working tree contains only intentional changes | `git status` clean after each fix commit |

## 11. Known limitations (unchanged MVP scope)

- Ideal Loads only; EnergyPlus object defaults for the system; no detailed HVAC (deferred by plan).
- Heating-only / cooling-only conditioning unsupported (both setpoint profiles required) —
  LadybugTools parity, documented.
- Latent equipment gains, humidification/dehumidification, aperture frames, face holes beyond
  apertures, DDY design days, north rotation: not converted (documented).
- Schedules are 365-day hourly `ScheduleFixedInterval`; leap years truncated to the first 8760
  hours with a warning (documented non-leap policy).
- Weekly semantics: day-composed profiles align to the run start weekday (EPW-derived or explicit
  option); flat sequences tile at their own period exactly as SAM does natively.

## 12. Remaining human validation

A real Rhino 8 Grasshopper smoke test was performed by the user on 2026-07-19: deployment
**passed** (Rhino 8 starts; Grasshopper loads the GHA; `SAMAnalytical.ToOpenStudio` and
`OpenStudio.RunModel` appear and execute; OSM/OSW created; OpenStudio CLI started; no native-DLL
or managed-assembly errors). The run itself failed in EnergyPlus — finding P1-06, now resolved.

**Outstanding:** a Rhino 8 **retest** of the exact same analytical model against this branch's
build (the new assemblies are deployed to `%APPDATA%\SAM`). The retest must be performed
interactively by the user; expected outcome: both components load, OSM/OSW generated, CLI exit
code 0, SQL present, fatal = 0, severe = 0, heating/cooling results returned, no R-value error.
This review does not claim it.

## 13. Merge recommendation

**READY pending the Rhino 8 retest.** All eight P1 findings (P1-01…P1-06 plus the two P2 items)
are resolved with regression tests; the final suite is **82/82** with four clean post-fix
EnergyPlus validations including an opaque-air-gap model. The only unresolved pre-PR item is the
user-performed Rhino 8 retest of the real model (§12). Do not open the PR until that retest
passes interactively in Rhino.
