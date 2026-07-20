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
| C3 | Fenestration, frames, constructions (FrameAndDivider, doors, hole diagnostics) | Pending | — |
| C4 | Site, weather, DDY, simulation settings (north, ground temps, run period, calendar) | Pending | — |
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

## Gates log

| Milestone | Build | Tests | EnergyPlus gate |
|---|---|---|---|
| Baseline | clean (0 errors) | 82/82 | SingleBox/AirGap/TwoBoxes end-to-end + CLI timeout kill |
| C0 | clean (docs only) | 82/82 | n/a (no code change) |
| C1 | clean (0 errors) | 93/93 | Full suite re-run incl. SingleBox/AirGap/TwoBoxes + CLI timeout |
| C2 | clean (0 errors; GH project excluded — Rhino running) | 104/104 | Latent + humidistat end-to-end (humidity output present, sane %RH range) |
