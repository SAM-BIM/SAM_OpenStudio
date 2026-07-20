# SAM → OpenStudio MVP — usage

Companion to [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md)
(binding contract) and [openstudio-mvp-status.md](openstudio-mvp-status.md) (run status).

## Prerequisites

- OpenStudio CLI + EnergyPlus. Discovery order: explicit `OpenStudioRunOptions.CliPath` →
  `openstudio.exe` on PATH → direct installations (`C:\openstudio-*`, `%ProgramFiles%\OpenStudio*`)
  → Ladybug Tools bundle (`%ProgramFiles%\ladybug_tools\openstudio`). Verified against
  **3.10.0** (matching the NuGet SDK).
- An EPW weather file, **or** WeatherData with hourly weather years embedded in the
  AnalyticalModel (`SAMAnalytical.CreateAnalyticalModelByAdjacencyCluster` with
  `_saveWeatherData_ = true`). Source precedence (never merged, always named in a diagnostic):

  ```
  Explicit EPW/DDY path
      overrides
  AnalyticalModel embedded WeatherData/design days
      overrides
  documented fallback or blocking diagnostic
  ```

  Annual weather: an explicit EPW wins; otherwise the embedded WeatherData is exported through
  SAM.Weather's `ToEPW` API (only when it carries weather years); otherwise its metadata
  (location, elevation/time zone, ground temperatures) is still used and a run reports the
  missing EPW as a blocking error while conversion-only stays valid with a warning.
  Design days: an explicit DDY (`OpenStudioConversionOptions.DdyPath`, overridden by
  `OpenStudioRunOptions.DdyPath`, or the Grasshopper `ddyPath_` input) wins; otherwise the
  embedded heating/cooling design days are translated (approximations are named in warnings);
  otherwise no design days. An explicit path that is supplied but unusable falls back to the
  embedded source with a warning naming the rejected path.
- Prebuilt SAM assemblies in `..\SAM\build\` (HintPath references).

## C# API

```csharp
using SAM.Analytical.OpenStudio;

// Convert + save + run the annual Ideal Loads simulation:
OpenStudioConversionResult result = analyticalModel.ToOpenStudio(
    epwPath: @"C:\weather\city.epw",
    outputDirectory: @"C:\sims\project01");        // OSM/OSW + run\ land here

// result.IsValid           — conversion had no error diagnostics
// result.Diagnostics       — structured SAM-OS-* codes (never silent substitution)
// result.ObjectMap         — SAM Guid → deterministic OpenStudio name
// result.RunResult         — exit code, eplusout.err severe/fatal, SQL path
// result.Loads             — annual kWh per zone + totals (finite-checked)

// Convert only (no CLI): pass run: false — OSM and OSW are still saved.
// Geometry-only conversion (no weather/settings/save):
OpenStudioConversionResult modelOnly = analyticalModel.ToOpenStudio();

// Run an existing OSM/OSW:
var runResult = OpenStudioSimulationRunner.Run(
    @"C:\sims\model.osm", epwPath, outputDirectory, null,
    out OpenStudioLoadSummary loads, diagnostics);

// Options:
var conversionOptions = new SAM.Core.OpenStudio.OpenStudioConversionOptions
{
    FirstDayOfWeek = DayOfWeek.Monday,   // weekly-profile calendar override; default: from the EPW
    // Post-MVP (completeness programme): NorthAngleDegrees, RunPeriodBegin/End, TimestepsPerHour,
    // SolarDistribution, ShadowCalculationFrequencyDays, CalendarYear, IsLeapYear (8784 schedules),
    // DaylightSavingsTime (default off), DdyPath + ImportAllDesignDays + RunSizingPeriods,
    // OutputVariableFrequency
};
var runOptions = new SAM.Core.OpenStudio.OpenStudioRunOptions
{
    CliPath = @"C:\Program Files\ladybug_tools\openstudio",  // explicit CLI (exe or install dir)
    TimeoutSeconds = 3600,  // real timeout: the process tree (CLI + EnergyPlus) is killed on expiry
    UseUniqueRunDirectory = true,  // default: GUID subdir per run — concurrent runs never collide
    ExtractTimeSeries = false,     // true loads hourly temperature/operative/humidity series
};

// Asynchronous, cancellable execution (token and timeout share the process-tree kill path):
var cts = new CancellationTokenSource();
OpenStudioConversionResult result2 = await analyticalModel.ToOpenStudioAsync(
    epwPath, outputDirectory, conversionOptions, runOptions, run: true,
    progress: new Progress<SAM.Core.OpenStudio.OpenStudioSimulationProgress>(p => Console.WriteLine(p)),
    cancellationToken: cts.Token);

// Engine-neutral results (result.Results): annual kWh per zone, peak kW + hour-of-year,
// unmet hours, gains breakdown, optional hourly series, runtime, warning/severe/fatal counts.
// SAM mapping: result.Results.ToSAM(analyticalModel) → AnalyticalModelSimulationResult;
// result.Results.ToSAM_SpaceSimulationResults(analyticalModel) → per-LoadType space results.
using (result) { /* result owns the OpenStudio model — dispose when done */ }
```

## Grasshopper components (SAM → OpenStudio tab)

| Component | Purpose |
| --- | --- |
| `SAMAnalytical.ToOpenStudio` | AnalyticalModel + output folder (+ optional `_epwPath`, `ddyPath_`, `_run`) → OSM/OSW paths, SQL path, heating/cooling kWh, diagnostics, success. Weather/design-day sources: explicit paths override embedded model data |
| `OpenStudio.RunModel` | Existing OSM/OSW (+EPW for OSM) → run, SQL path, heating/cooling kWh, diagnostics |
| `OpenStudio.CreateSpaceSimulationResultsBySQL` (pre-existing) | SQL → SAM `SpaceSimulationResult` objects |
| `OpenStudioCreateDesignDaysBySQL`, `SAMAnalyticalAddResultsBySQL` (pre-existing) | Design days / result attachment from SQL |

Components are thin wrappers — every conversion rule lives in the tested
`SAM.Analytical.OpenStudio` API. Both components execute **non-blocking** (background task,
`cancel_` input, no stale outputs, no UI-thread freeze).

## Post-MVP (analytical completeness) notes

See [openstudio-analytical-completeness-status.md](openstudio-analytical-completeness-status.md)
for the full programme. The following MVP limitations are **resolved**: single-mode
(heating-only/cooling-only) thermostats, latent equipment gains, humidification/dehumidification
(ZoneControlHumidistat + Ideal Loads humidity control), SpaceType outdoor air with ventilation
schedules, native ACH infiltration, aperture frames (WindowPropertyFrameAndDivider with pane
geometry), SimpleGlazingSystem fallback for layerless performance-parameter glazing, hole
diagnostics with GUID + geometry summary, north rotation, ground temperatures (SAM → EPW →
documented default), DDY design days with sizing enablement and environment-filtered annual
results, leap-year schedules, custom run period/timestep/calendar, rich results extraction
(peaks, unmet hours, gains, series) with SAM result mapping, and cancellable asynchronous runs
in unique directories.

## Remaining documented limitations

- Ideal Loads only (no detailed HVAC); EnergyPlus object defaults for the Ideal Loads system
  except humidity control (wired from SAM humidity profiles).
- A zero heating or cooling value in `OpenStudioLoadSummary` is a **genuine result**, not a
  missing one: every reported zone has a full time series in the SQL output. A zone that failed
  to report is omitted from the per-zone dictionaries — zero and missing are never conflated.
- Glazing dividers/muntins: N/A (no SAM source data). Blinds/shades and opening properties:
  unsupported with structured diagnostics (no geometry in SAM; HVAC domain).
- Emitter characteristics, exhaust flows, ventilation-system equipment: deferred to the
  detailed-HVAC programme (SAM-OS-HVAC-001/SAM-OS-IC-001 diagnostics).
- STAT ground-temperature parsing: deferred (SAM WeatherData → EPW header → named 18 °C default).
- Daylight saving: option-driven, default OFF (SAM carries no DST data).
- SAM hourly design days → parametric `SizingPeriod:DesignDay` is an approximation (used when
  no DDY path exists: daily range from the 24 h spread, constant dew point at the max-dry-bulb
  hour, mean wind/pressure, ASHRAEClearSky with clearness 0.0 heating / 1.0 cooling — every
  approximation is named in a warning); the DDY import is the deterministic primary path.
- Self-intersecting polygons are rejected with a diagnostic (never auto-repaired).
- Glazing optical sides follow the EnergyPlus definition (Front = side opposite the zone):
  SAM `External*` → Front, `Internal*` → Back — deliberately different from the SAM_LadybugTools
  exporter (see `SAM_OPENSTUDIO_MATERIAL_MAPPING.md`).
- Gas cavities are usage-dependent (see `SAM_OPENSTUDIO_MATERIAL_MAPPING.md`): a SAM
  `GasMaterial` in an **opaque** `Construction` becomes an `OS:Material:AirGap` resistive layer
  with R = 1/h from SAM's `Heat Transfer Coefficient` [W/m²K] (missing/invalid conductance is a
  SAM-OS-MAT-001 error, never a zero-resistance layer); in an `ApertureConstruction` pane it
  remains `OS:WindowMaterial:Gas` (gas type + thickness).
- SpaceType names embed a deterministic content hash over the condition AND the per-space
  computed densities (`SAM_InternalCondition_<Name>_<hash8>`): shared conditions never impose
  one space's densities on another.
