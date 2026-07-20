# SAM → OpenStudio Analytical Completeness v1 — Status

Programme: complete every non-HVAC part of `SAM.Analytical.AnalyticalModel` translation with a
defensible OpenStudio/EnergyPlus representation — native where possible, controlled approximation
where necessary, structured diagnostic where unsupported, never silently dropped. Ideal Loads
remains the simulation system. Branch: `feature/openstudio-analytical-completeness` (base
`sow/2026-Q3`, MVP baseline `bf75bf3`, 82/82 tests).

| Milestone | Scope | Status | Commit |
|---|---|---|---|
| C0 | Complete SAM analytical coverage audit (Markdown + machine-readable manifest) | **Done** | — |
| C1 | Core adapter hardening (P3 items, disposal, statistics, SQL, VersionTranslator) | **Done** | — |
| C2 | Internal conditions, schedules, conditioning modes (latent, humidistat, single-mode) | **Done** | — |
| C3 | Fenestration, frames, constructions (FrameAndDivider, doors, hole diagnostics) | **Done** | — |
| C4 | Site, weather, DDY, simulation settings (north, ground temps, run period, calendar) | **Done** | — |
| C5 | Results extraction and SAM result mapping (engine-neutral schema, units authority) | **Done** | — |
| C6 | Cancellation, asynchronous execution, Grasshopper UX | **Done** | — |
| C7 | Full regression suite, completeness enforcement, documentation | **Done** | — |

## C0 — Coverage audit (done)

- `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md` — full audit of the SAM analytical data surface;
  tables generated from the manifest.
- `tests/resources/openstudio-analytical-coverage.json` — machine-readable manifest (277 entries
  after the Stage L review removed two stale `AnalyticalMaterialParameter` rows, review P2-01)
  consumed by the C7 completeness tests (tests never parse the Markdown).
- Baseline verified on this machine before work started: x64 Debug build clean,
  **82/82 tests passing** (including the EnergyPlus simulation tests), OpenStudio CLI
  3.10.0+86d7e215a1, OpenStudio NuGet 3.10.0 restored.
- Classification counts at C0: Native 135, Derived 28, Approximated 21, Unsupported 20,
  Deferred 17, NA 58 across 279 entries (MVP already covers 90). The Stage L review (P2-01)
  found the C0 audit had transcribed two commented-out `SAM.Analytical.MaterialParameter`
  members (`TypeName`, `Description`); the corrected manifest carries 277 entries, NA 56.
- Agreed reclassifications (binding): dividers → NA (no SAM source data); blinds/shades and
  opening properties → Unsupported with diagnostics; STAT parsing → deferred; SAM hourly design
  days → Approximated parametric fit (DDY import is the deterministic primary path); subhourly
  profiles → NA (SAM profiles are hour-indexed); daylight saving → option, default off; emitter
  radiant proportions → Deferred; pollutant → Unsupported.

## C1 — Adapter hardening (done)

- `Query/IsConditioned.cs` — culture-independent ordinal case-insensitive classification
  (tr-TR regression test).
- `Convert/ToOpenStudio/Profile.cs` + context — schedule cache keyed by (Guid, ProfileType);
  the same profile under two types yields two schedules with correct type limits.
- `Convert/ToOpenStudio/InternalCondition.cs` — SpaceType share key now folds the per-space
  computed load densities into the content hash; a shared condition no longer imposes the first
  space's densities on other spaces.
- `Convert/ToOpenStudio/BuildingStory.cs` — stories fall back to space minimum elevations when
  no floor-group panels exist (Information diagnostic).
- `Convert/ToOpenStudio/AnalyticalModel.cs` — conditioned spaces with no valid surfaces are
  rejected (Error, no thermostat/Ideal Loads); skip sites feed the statistics.
- `Classes/OpenStudioConversionResult.cs` — implements `IDisposable` (owns the Model) and
  carries an immutable `Statistics` snapshot.
- New `SAM.Core.OpenStudio/Classes/OpenStudioConversionStatistics.cs` — source/created/skipped/
  unsupported + per-severity counts, maintained by the context.
- `Classes/OpenStudioSimulationRunner.cs` — the CLI is never launched when the conversion
  reported errors (OSM/OSW still saved); annual-energy extraction uses parameterised
  System.Data.SQLite commands (no SQL string concatenation).
- `SAM.Core.OpenStudio/Create/Model.cs` — OSM load via `VersionTranslator` (older files upgrade
  instead of being rejected); binding probe passed at build time.
- Tests: +11 (`tests/.../C1/AdapterHardeningTests.cs`) — 82 → **93**.

## C2 — Internal conditions, schedules, conditioning (done)

- `InternalCondition.cs` — latent equipment as a dedicated `ElectricEquipment` instance
  (Fraction Latent = 1, own schedule); infiltration uses the native ACH field when the
  condition carries `InfiltrationAirChangesPerHour` (flow-per-exterior-area remains the
  fallback); SpaceType `DesignSpecificationOutdoorAir` (method Sum, per-person/per-area/ACH/
  absolute supply parameters) with the ventilation profile as the outdoor-air schedule —
  `SpaceParameter.OutsideSupplyAirFlow` keeps precedence via the EnergyPlus space-over-type
  rule; pollutant and TAS ventilation-function data raise structured diagnostics.
- `Thermostat.cs` — single-mode thermostats: only the existing setpoint schedule is set
  (EnergyPlus SingleHeating/SingleCooling; no invented setpoints); both-missing stays an error.
- New `Convert/ToOpenStudio/Humidistat.cs` — `ZoneControlHumidistat` from the
  Humidification/Dehumidification profiles (Percent type limits 0–100); named-but-unresolved
  profile → SAM-OS-SCH-001 error.
- `IdealLoads.cs` — humidity control types set to Humidistat where the matching schedule
  exists; documented EnergyPlus defaults otherwise.
- `Profile.cs` — Percent schedule type limits; per-type range warnings.
- `SimulationSettings.cs` — `Zone Air Relative Humidity` added to the requested outputs.
- Docs: INTERNAL_CONDITION_MAPPING updated; the M5 infiltration test was updated to the native
  ACH basis (manifest-bound change).
- Note: builds currently exclude the Grasshopper project while Rhino is running on this
  machine (post-build copy to `%APPDATA%\SAM` locks); the analytical/test chain builds clean.
- Tests: +11 (`tests/.../C2/InternalConditionCompletenessTests.cs`) — 93 → **104**.

## C3 — Fenestration, frames, constructions (done)

- `Aperture.cs` — `WindowPropertyFrameAndDivider` from SAM frame data: width from
  `DefaultFrameWidth` else SAM frame thickness; conductance U = 1/Σ(t/λ) over the frame layers;
  solar/visible absorptance = 1 − External*Reflectance of the outermost frame layer. With a
  frame applied the SubSurface polygon becomes the SAM **pane** polygon (EnergyPlus grows the
  frame outward); area conservation (pane strictly inside the aperture) is validated. Invalid/
  incomplete frame data → frameless full-polygon fallback + SAM-OS-CON-002 warning (never wrong
  geometry); frames on opaque doors skipped (info). Opening properties, feature shades and TAS
  additional-heat-transfer percentages raise structured diagnostics (SAM-OS-CON-002).
- `Material.cs` — `TransparentMaterialParameter.IsBlind` raises SAM-OS-MAT-002 info; converted
  as plain glazing (no fabricated `WindowMaterial:Blind`).
- `Panel.cs` — hole diagnostics carry the panel Guid + geometry summary (count, per-hole and
  total area); holes are never fabricated into windows. (Manifest row aligned: SAM-OS-GEO-001,
  not GEO-002.)
- `SubSurfaceType.cs` — verified: opaque door vs glazed door by pane material family (tests).
- New diagnostic codes SAM-OS-CON-002 (unsupported construction/aperture parameter) and
  SAM-OS-MAT-002 (unsupported material parameter).
- Fixtures: `CreateFramedWindowConstruction`, frame material in `CreateMaterialLibrary(bool)`,
  `SingleBox(apertureConstructionOverride, includeFrameMaterial)`, `TwoAdjacentBoxes(sharedWallWindow)`.
- Docs: MATERIAL_MAPPING updated.
- Tests: +10 (`tests/.../C3/FenestrationTests.cs`) — 104 → **114**.

## C4 — Site, weather, design days, simulation settings (done)

- `SimulationSettings.cs` — north axis from `AnalyticalModelParameter.NorthAngle`
  (**radians → degrees**, convention resolved from SAM_Tas `ToT3D/ToTBD Building.cs`; explicit
  `NorthAngleDegrees` option overrides); explicit RunPeriod; timestep option; solar
  distribution (via `SimulationControl` — OpenStudio exposes the E+ Building field there);
  `ShadowCalculation` frequency option; YearDescription (first day of week from the run
  calendar, calendar year, leap year — flattened onto `Model` in the C# wrapper); DST option,
  **default off** (object removed when present); sizing control enabled when design days are
  imported (`RunSizingPeriods` overrides).
- `Weather.cs` — SAM Location takes precedence over the EPW site coordinates (information
  diagnostic; time zone stays with the EPW). Ground temperatures: SAM WeatherData
  (nearest-to-surface set) → EPW header via **SAM.Weather's native parser** (verified:
  OpenStudio's `setWeatherFile` does NOT import them) → warning naming the E+ 18 °C default.
- `Query/DesignDays.cs` — stub completed (IdfFile → EnergyPlusReverseTranslator).
  New `Convert/ToOpenStudio/DesignDays.cs` — DDY import with 99.6%/0.4% name filter
  (`ImportAllDesignDays` option; fallback to all + warning); `OpenStudioRunOptions.DdyPath`
  wired (takes precedence over the conversion option).
- `OpenStudioSimulationRunner.ReadAnnualEnergy` — annual sums restricted to the weather-run
  environment (EnvironmentType 3) so imported design days never double-count; defensive
  fallback when the table/row is absent.
- `Profile.cs` — leap-year schedule generation (8784 values: ≥8784 stores pass through,
  8760 stores repeat 31 Dec, day-composed profiles tile 366 days).
- `OpenStudioConversionOptions` — new options (NorthAngleDegrees, RunPeriod*, TimestepsPerHour,
  SolarDistribution, ShadowCalculationFrequencyDays, CalendarYear, IsLeapYear,
  DaylightSavingsTime, DdyPath, ImportAllDesignDays, RunSizingPeriods, OutputVariableFrequency).
- Binding probes passed at build: `ZoneControlHumidistat` (C2), `WindowPropertyFrameAndDivider`
  (C3), `SiteGroundTemperatureBuildingSurface`, `YearDescription` (on Model),
  `RunPeriodControlDaylightSavingTime`, `ShadowCalculation`, `DesignDay`/
  `EnergyPlusReverseTranslator`, `SimulationControl.setSolarDistribution`.
- Tests: +10 (`tests/.../C4/SiteWeatherSettingsTests.cs`) — 114 → **124**.

## C5 — Results extraction and SAM mapping (done)

- New `Classes/OpenStudioSimulationResultSet.cs` — engine-neutral result set: annual energies
  [kWh], per-zone and coincident building peaks [kW] with hour-of-year, unmet hours, gains
  breakdown (people/lighting/equipment/window-solar/net-infiltration/ventilation), optional
  hourly temperature/operative/humidity series (`OpenStudioRunOptions.ExtractTimeSeries`),
  CLI runtime, warning/severe/fatal counts.
- `OpenStudioSimulationRunner.cs` — `ExtractResultSet`: parameterised SQL throughout,
  weather-run environment filtering (peaks included), runtime measured, err-warning counts.
  **Zone identity normalisation**: Ideal Loads variables key on the system name, zone-level
  variables on the ThermalZone name, enclosure variables on the Space name — all remapped onto
  the energy key by the shared SAM Guid suffix (verified against the live SQL).
- `SAM.Core.OpenStudio/Query/ConvertUnit.cs` — one unit authority: `JoulesToKilowattHours`
  (annual), `JoulesPerIntervalToWatts` (peaks/at-peak); the ÷3.6e6-vs-÷3600 divergence is
  resolved by role, not by convention.
- `SimulationSettings.cs` — output variables for unmet hours, gains and window solar
  (`Enclosure Windows Total Transmitted Solar Radiation Energy` — the `Zone Windows …` name
  was retired in current EnergyPlus; found via eplusout.rdd inspection).
- New `Convert/ToSAM/SimulationResults.cs` — `AnalyticalModelSimulationResult`
  (consumption/peaks/area/volume) and per-space per-LoadType `SpaceSimulationResult`
  (Load [W], LoadIndex [h], UnmetHours) with case-insensitive deterministic-name matching.
- New `docs/SAM_OPENSTUDIO_RESULT_MAPPING.md`.
- Bug found by the gate: an hour-of-year day-clamp (29–31 → 28) inflated coincident peaks;
  fixed with `DaysInMonth`.
- Tests: +6 (`tests/.../C5/ResultsExtractionTests.cs`) — 124 → **130**.

## C6 — Cancellation, async, Grasshopper (done)

- `OpenStudioSimulationRunner.cs` — `CancellationToken` on both Run paths (token and timeout
  share the existing Job-Object tree-kill); `RunAsync` / `Convert.ToOpenStudioAsync`;
  `IProgress<OpenStudioSimulationProgress>` stages (SavingOsm → WritingOsw → RunningCli →
  ReadingResults → Complete); **unique GUID run directories by default**
  (`OpenStudioRunOptions.UseUniqueRunDirectory`) with an in-progress lock-file guard +
  deterministic cleanup for non-unique directories; thread-safe diagnostics (context lock).
- New `SAM.Core.OpenStudio/Classes/OpenStudioSimulationProgress.cs` (+ stage enum).
- New local GH base `GH_SamAsyncComponent` (Grasshopper project — no task-capable base exists
  in the SAM ecosystem and SAM core is untouched): input-signature gating (changed inputs
  cancel the stale run — never duplicate runs), optional `cancel_` input, stale-output
  clearing at run start, document-reschedule harvest on the UI thread, no native OpenStudio
  objects across the boundary, results disposed after harvest. Both components
  (`SAMAnalyticalToOpenStudio`, `OpenStudioRunModel`) migrated.
- Rhino-process exit kills orphaned runs by design (kill-on-close Job Object).
- GH/Rhino interaction itself is validated by a human in Rhino 8 (see C7 validation steps);
  everything below the component shell is test-covered headless.
- Tests: +6 (`tests/.../C6/CancellationAsyncTests.cs`) — 130 → **136**.

## C7 — Regression, validation, documentation (done)

- **Completeness enforcement** (`tests/.../C7/CompletenessTests.cs`): the machine-readable
  manifest is enforced — every live member of the 16 covered enums has an entry (reflection;
  fails on silent omission of a known property), every declared diagnostic code exists in
  `OpenStudioDiagnosticCodes`, the manifest is structurally sound (unique ids, valid
  statuses/milestones, documented policies for Derived/Approximated/Unsupported entries).
  Clean fixtures drop nothing (0 skips, 0 unsupported diagnostics); unsupported data raises
  exactly the declared diagnostics.
- **Missed C3 item recovered**: the SimpleGlazingSystem fallback (aperture constructions
  without pane layers but with U/SHGC/VT performance parameters; aperture-level values take
  precedence) is implemented in `Construction.cs` with two tests. New diagnostic code
  `SAM-OS-RUN-002` (weather/design-day data) added per the manifest.
- **Determinism**: the same model converted twice yields identical object sets.
- **Repeated conversion + disposal**: 5× loop clean; repeated run + disposal covered in C6.
- **Performance** (this machine, Debug x64): TwoAdjacentBoxes conversion ≈ 0.4 s, 76 model
  objects; SingleBox end-to-end annual run ≈ 2–4 s CLI wall-clock. MVP object counts unchanged
  (pinned by the M3/M4 tests); the MVP suite ran ≈ 20 s at C0 vs ≈ 90 s now — the delta is the
  ~30 additional E+ simulations, not conversion cost.
- **Simulation table** (all green, `[Category("Simulation")]`):

  | Gate | Asserts |
  |---|---|
  | M6 SingleBox / AirGap / TwoBoxes + CLI timeout | end-to-end energy, no fatal/severe, tree kill |
  | C2 latent + humidistat | humidity output present and sane |
  | C3 framed window | frame in IDF; E+ glass area = pane area |
  | C4 design days ×2, north rotation ×2 | sizing on: annual unchanged; solar-gain shift |
  | C5 extraction ×6 runs | peaks/unmet/gains/series/units/design-day exclusion |
  | C6 cancellation ×6 runs | cancel/parallel/collision/sequential/progress |

- **Production model**: none supplied on this machine — production validation is deferred to
  the human Rhino stage below (largest synthetic fixtures validated instead).
- Docs: this file, `SAM_OPENSTUDIO_RESULT_MAPPING.md` (C5), usage guide and README updated.

## Human validation steps (Rhino 8)

1. Build the solution x64 Debug with Rhino **closed** (GH post-build copies to `%APPDATA%\SAM`).
2. Open Rhino 8 → Grasshopper → confirm `SAMAnalytical.ToOpenStudio` and `OpenStudio.RunModel`
   load from the SAM category.
3. Feed a production SAM analytical model JSON + EPW: confirm the component reports "Running"
   without blocking the UI, completes onto the outputs, honours `cancel_`, and that no
   `openstudio.exe`/`energyplus.exe` processes survive a Rhino close mid-run.
4. Compare `heating`/`cooling` and the diagnostics list against the TAS production route for
   the same model; attach findings to the PR review.

## Gates log

| Milestone | Build | Tests | EnergyPlus gate |
|---|---|---|---|
| Baseline | clean (0 errors) | 82/82 | SingleBox/AirGap/TwoBoxes end-to-end + CLI timeout kill |
| C0 | clean (docs only) | 82/82 | n/a (no code change) |
| C1 | clean (0 errors) | 93/93 | Full suite re-run incl. SingleBox/AirGap/TwoBoxes + CLI timeout |
| C2 | clean (0 errors; GH project excluded — Rhino running) | 104/104 | Latent + humidistat end-to-end (humidity output present, sane %RH range) |
| C3 | clean (0 errors; GH project excluded — Rhino running) | 114/114 | Framed window end-to-end (frame in IDF; E+ glass area = pane area 2.47 m²) |
| C4 | clean (0 errors; GH project excluded — Rhino running) | 124/124 | Design-day run (sizing on): annual results unchanged vs baseline (environment filter proven); north rotation 0°→180° shifts heating/cooling as expected |
| C5 | clean (0 errors; GH project excluded — Rhino running) | 130/130 | Extraction validated on annual + sizing-enabled runs (peaks, unmet, gains, series, units, design-day exclusion) |
| C6 | clean (0 errors, **full solution incl. GH** — Rhino closed) | 136/136 | Cancel-before/cancel-during (tree kill, no surviving process), parallel unique dirs, same-dir collision, sequential, progress stages |
| C7 | clean (0 errors, full solution) | **146/146** | Full suite (~30 E+ simulations), manifest completeness enforcement, determinism, disposal, performance |
| Review fixes (pre-Stage-C) | clean | 155/155 | `1b8cbc0` System.Data.SQLite deployment (Ideal Loads results readable); `e48352b` wind-panel outward normals |
| Stage L (review corrections) | clean (0 errors, full solution incl. GH — Rhino closed) | **170/170, 0 skipped** | Five post-fix scenario simulations (latent/humidistat, framed window, design days, north rotation, rich extraction) + cancellation tree-kill, parallel runs, disposal loop, determinism, coverage enforcement, largest fixture — all green |

## Stage L — independent review corrections (2026-07-20)

The independent review ([openstudio-analytical-completeness-review.md](openstudio-analytical-completeness-review.md))
confirmed no P0, three P1 and six P2 findings; every P1 and every small in-scope P2 is fixed
in its own commit with a failing-first regression test:

| Commit | Finding | Correction |
|---|---|---|
| `dda59b0` | P1-01 | 18 declared-but-never-emitted coverage diagnostics now fire at the InternalCondition / Aperture / Construction / Panel / Material conversion sites (8 new tests) |
| `600ef2b` | P1-02 | Default DDY import selects exactly the ASHRAE heating 99.6% + cooling .4% pair (the cooling day was never matched before; `Hum_n`/`Wind` days leaked in) |
| `ec7123f` | P1-03 | SQL hour-of-year follows the run calendar in leap years (Feb 29 no longer collapses onto Feb 28; coincident peaks no longer double-count) |
| `e5e72ff` | P2-01 | Stale-manifest-id reverse check — immediately caught 2 real stale rows; manifest corrected to **277 entries / NA 56** |
| `ff93d89` | P2-02 | `SAM-OS-RUN-003` warning when a non-hourly output frequency would mis-scale extracted peaks (frequency-aware extraction stays follow-up) |
| `d631e18` | P2-03 | SAM Location site override validated (NaN/out-of-range keeps the EPW site with a warning) |
| `68adfa4` | P2-05 | Grasshopper cancels the simulation on component removal and document close; completion continuation never targets a disposed document (human Rhino step 5) |
| `68d09f5` | P2-06 | Coverage MD/JSON encoding repaired (16 double-encoded sequences + 1 invalid byte) — both strict UTF-8 |

P2-04 (standalone `.osw` run-directory isolation) is a recorded follow-up.

## Human-Rhino validation corrections — embedded weather/design days (done)

Grasshopper validation showed the conversion only ever used explicit file inputs: the
`SAMAnalytical.ToOpenStudio` component passed `null` options and required `_epwPath`, and the
`AnalyticalModelParameter.HeatingDesignDays`/`CoolingDesignDays` collections had no code
consumer at all. Source-resolution policy implemented (one coherent contract):

```
Explicit EPW/DDY path
    overrides
AnalyticalModel embedded WeatherData/design days
    overrides
documented fallback or blocking diagnostic
```

- `Query/EmbeddedAnnualWeatherPath.cs` — SAM WeatherData retains no source EPW path, but the
  existing `Weather.Convert.ToEPW` export is verified and used when the embedded WeatherData
  carries weather years (deterministic content-hash temp file, reused across runs).
- `Convert/ToOpenStudio/AnalyticalModel.cs` — weather/design-day source resolution with
  Information diagnostics naming the selected source ("Annual weather source: explicit EPW /
  AnalyticalModel WeatherData"; "Design-day source: explicit DDY / AnalyticalModel
  heating/cooling design days / no design-day source supplied"). Explicit and embedded design
  days are never merged. A supplied-but-unusable explicit path falls back to the embedded
  source with a warning naming the rejected path. `OpenStudioConversionContext.EpwPath` carries
  the effective path to the OSW/runner.
- `Convert/ToOpenStudio/DesignDays.cs` — embedded SAM hourly design days →
  `SizingPeriod:DesignDay`: month/day validated; max dry bulb and daily range from the 24 h
  profile; humidity approximated as a constant dew point at the max-dry-bulb hour (Magnus);
  mean wind speed + circular-mean direction; daily-mean barometric pressure; ASHRAEClearSky
  with clearness 0.0 (heating/WinterDesignDay) / 1.0 (cooling/SummerDesignDay). Every
  approximation is a named warning — a day without a dry-bulb profile is skipped, never
  silently downgraded.
- `Convert/ToOpenStudio/Weather.cs` — run-aware weather assignment: a missing annual EPW
  source is a blocking **error** only when `_run = true`; conversion-only saves OSM/OSW with a
  warning. Without any EPW the embedded WeatherData still supplies site location/elevation/time
  zone and ground temperatures (an explicit EPW wins over this metadata; the SAM model Location
  still overrides any weather source, P2-03 unchanged).
- `Query/EmbeddedWeatherFingerprint.cs` — deterministic content fingerprint (SAM JSON with
  volatile Guids stripped → SHA-256) of the embedded WeatherData + design days; the
  Grasshopper async signature keys on it, so changing embedded content on the same model Guid
  triggers a new conversion.
- `SAMAnalytical.ToOpenStudio` (component 1.2.0): `_epwPath` now optional; new optional
  `ddyPath_` input appended after `cancel_` (archived documents pair inputs by position —
  appending keeps existing wiring); real `OpenStudioConversionOptions` with `DdyPath` is passed
  instead of null; signature = model Guid + EPW + DDY + output + run + embedded fingerprint.
- SAM-core note (separate change required, **not** fixed here):
  `SAMAnalytical.CreateAnalyticalModelByAdjacencyCluster` persists `weatherData_`,
  `coolingDesignDays_`, `heatingDesignDays_` only when `_saveWeatherData_ = true`
  (persistent default **false**), so models saved with defaults carry no embedded weather. The
  required SAM change is to default `_saveWeatherData_` to true (or persist whenever the inputs
  are supplied) — see the PR discussion.
- Tests: +11 (`tests/.../C4/EmbeddedWeatherDesignDaysTests.cs`) — 170 → **181**.

## Human-Rhino validation corrections — EnergyPlus SQL timestamps (done)

`OpenStudio.CreateDesignDaysBySQL` threw `Hour, Minute, and Second parameters describe an
un-representable DateTime` on a real `eplusout.sql`: raw SQL fields went straight into a
`DateTime` constructor (`TimeIndexDictionary.cs` did `hour - 1`, so the hour-0 sub-hourly rows
every EnergyPlus timestep output writes produced hour −1; `Query/DateTime.cs` received hour 24
rows unmodified).

- **Encoded convention** (verified against the live SQL): the `Time` fields denote the **end
  of the reporting interval** — `Hour` 0–24, `Minute` 0–60, `Year` 0 on sizing/design-day
  environments. `24:00` is midnight at the end of the day; minute 60 is the full hour;
  `(0, 10)` is the interval ending 00:10.
- New authoritative helper `SAM.Core.OpenStudio/Query/TryGetDateTime.cs` — validates the
  calendar date (February 29 under a non-leap calendar, month/day 0, hour > 24, minute > 60 →
  structured diagnostic, never a throw, never clamped) and normalises by `TimeSpan` addition,
  so day/month/year rollover (December 31 24:00, leap years) is DateTime arithmetic.
  `IntervalHourOfYear` maps an interval-end timestamp to the 0-based interval index by
  stepping one tick back into the interval (matches the legacy hourly convention exactly and
  extends it to sub-hourly rows).
- `TimeIndexDictionary.cs` rewritten on the helper (the inverted/unused `year_Temp` default
  logic removed) with an optional diagnostics sink — malformed rows are skipped and reported.
  `Query/DateTime.cs` normalises through the helper and returns null on malformed rows.
- `Create/DesignDays.cs` — annual weather-run environments (EnvironmentType 3) are no longer
  misread as design days; hourly indices come from interval-start semantics (the 24:00 row
  stays with its design day); missing series/TimeIndex values are skipped instead of throwing
  `KeyNotFoundException`; days are classified heating/cooling from the environment name; a
  `diagnostics` out-parameter reports skipped rows and the no-design-days case.
- `Create/SpaceSimulationResults.cs` — peak/max/min hour indices use `IntervalHourOfYear`.
- `OpenStudio.CreateDesignDaysBySQL` (component 1.0.2) — malformed SQL rows surface as
  structured runtime warnings, never a solution exception.
- Tests: +18 (`tests/.../C5/EnergyPlusTimestampTests.cs`: ordinary/hour-24/minute-60/
  hour-24+minute-60/sub-hourly-hour-0 rows, year-0 sizing rows, Feb 28, Feb 29 leap and
  non-leap, Dec 31 final timestep, month-0 warmup, duplicate hour-boundary rows, synthetic
  sizing-environment SQL end-to-end, annual-only SQL with the live edge rows, plus the real
  30 MB reproduction fixture gated on `SAM_OPENSTUDIO_TEST_SQL`) — 181 → **199**.

## Human-Rhino validation corrections — AddResultsBySQL result mapping (done)

`SAMAnalytical.AddResultsBySQL` returned no space/panel results. Three confirmed defects:
(1) the annual family was never read — only design-day `ZoneSizes` data was mapped, so an
annual-only SQL produced area/volume-only results; (2) `Create.SurfaceSimulationResults`
replaced the per-surface list with an empty one whenever no space result carried a load time
index (always the case without sizing runs); (3) EnergyPlus names
(`SAM_ThermalZone_..._<guid8>`, `SAM_Surface_..._<guid8>`) were matched against full
32-character Guids, so nothing ever related back to a SAM space or panel. The Grasshopper
AnalyticalModel path additionally mutated the source model's cluster in place.

- `Modify.AddResults(AdjacencyCluster, path, out diagnostics)` rewritten: the annual family
  goes through the single authoritative C5 reader (`OpenStudioSimulationRunner.ExtractResultSet`
  → `ToSAM_SpaceSimulationResults`, new `IEnumerable<Space>` overload — no second SQL
  implementation); the design-day `ZoneSizes` family is kept when sizing ran; surface results
  keep one `SurfaceSimulationResult` per engine surface (an internal panel's two engine
  surfaces produce two results on the same panel — never summed). Zone/surface names resolve
  by deterministic Guid suffix (`Query.TryGetGuidSuffix`/`GuidSuffix`), then full-Guid
  reference, then sanitized name — never by display name alone.
- Zero-vs-missing: a genuine zero peak stays a valid result; only a zone absent from the SQL
  is diagnosed. Unmatched SQL zones/surfaces and result-less SAM spaces/panels are both
  reported through structured diagnostics surfaced as Grasshopper warnings.
- Rerun safety: identical results (type, name, reference, load type) from the same source are
  not duplicated; relations are ensured.
- `SAMAnalytical.AddResultsBySQL` (component 1.0.5): clones the input cluster before attaching
  (non-mutating convention); `panelSimulationResults` output name retained (SAM class
  `SurfaceSimulationResult`, documented); diagnostics surfaced; version bump.
- `Create/PanelSimulationResults.cs`: the per-surface base list survives when no space result
  carries a load time index (the empty-list defect).
- Tests: +9 (`tests/.../C5/AddResultsBySqlTests.cs`: one-zone attach+relate, two-zone internal
  panel identity, duplicate sanitized names, zero-vs-missing, rerun dedup, annual+design-day
  separation, save/reload serialization, non-mutating component path, real-fixture gate) —
  199 → **208**. Result-mapping doc updated with the attachment/aggregation rules.

## Final summary

- Tests: 82 (MVP) ��' 146 (C7) ��' 170 (Stage L) ��' **208 after the human-Rhino validation
  corrections** (0 skipped); every milestone and every review fix gated by x64 Debug build +
  full suite + E+ runs.
- Coverage (277 manifest entries after review P2-01 removed two stale rows): **Native 135,
  Derived 28, Approximated 21, Unsupported 20, Deferred 17, NA 56**. Translated-or-diagnosed:
  every entry with energy semantics has a Native/Derived/Approximated mapping or a declared
  Unsupported diagnostic; nothing is silently dropped (enforced by the C7 completeness tests
  against the manifest, in both directions since the Stage L review).
- Known limitations: dividers/muntins N/A (no SAM data); blinds/shades and opening properties
  unsupported (no geometry; HVAC domain); SAM hourly design days approximated (DDY import is
  the deterministic path); emitter characteristics and exhaust flows deferred to the HVAC
  programme (now reported with SAM-OS-HVAC-001 deferral diagnostics); STAT parsing deferred;
  DST default off; Ideal Loads remains the simulation system. Review follow-ups: standalone
  `.osw` run-directory isolation (P2-04) and frequency-aware peak extraction (P2-02).
- Branch `feature/openstudio-analytical-completeness`; no PR opened. Review recommendation:
  **READY** for human Rhino validation (checklist in the review document §10), then PR.
