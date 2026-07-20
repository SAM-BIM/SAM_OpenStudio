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
| C5 | Results extraction and SAM result mapping (engine-neutral schema, units authority) | Pending | — |
| C6 | Cancellation, asynchronous execution, Grasshopper UX | Pending | — |
| C7 | Full regression suite, completeness enforcement, documentation | Pending | — |

## C0 — Coverage audit (done)

- `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md` — full audit of the SAM analytical data surface;
  tables generated from the manifest.
- `tests/resources/openstudio-analytical-coverage.json` — machine-readable manifest (279 entries)
  consumed by the C7 completeness tests (tests never parse the Markdown).
- Baseline verified on this machine before work started: x64 Debug build clean,
  **82/82 tests passing** (including the EnergyPlus simulation tests), OpenStudio CLI
  3.10.0+86d7e215a1, OpenStudio NuGet 3.10.0 restored.
- Classification counts at C0: Native 135, Derived 28, Approximated 21, Unsupported 20,
  Deferred 17, NA 58 across 279 entries (MVP already covers 90).
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

## Gates log

| Milestone | Build | Tests | EnergyPlus gate |
|---|---|---|---|
| Baseline | clean (0 errors) | 82/82 | SingleBox/AirGap/TwoBoxes end-to-end + CLI timeout kill |
| C0 | clean (docs only) | 82/82 | n/a (no code change) |
| C1 | clean (0 errors) | 93/93 | Full suite re-run incl. SingleBox/AirGap/TwoBoxes + CLI timeout |
| C2 | clean (0 errors; GH project excluded — Rhino running) | 104/104 | Latent + humidistat end-to-end (humidity output present, sane %RH range) |
| C3 | clean (0 errors; GH project excluded — Rhino running) | 114/114 | Framed window end-to-end (frame in IDF; E+ glass area = pane area 2.47 m²) |
| C4 | clean (0 errors; GH project excluded — Rhino running) | 124/124 | Design-day run (sizing on): annual results unchanged vs baseline (environment filter proven); north rotation 0°→180° shifts heating/cooling as expected |
