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

## Reading `SAMAnalytical.AddResultsBySQL` output

The component returns the model with results attached, plus flat
`spaceSimulationResults` / `panelSimulationResults` lists. Two things about those lists are
worth knowing before you wire them up.

### Mapping a result back to its Space or Panel — use `Reference`

Every matched result carries the **SAM object's Guid** in its `Reference` property (32-hex,
no dashes — `Guid.ToString("N")`). That is the mapping key: group results by `Reference` and
match against `Space.Guid` / `Panel.Guid`. Do not parse the result `Name` — it is the
EnergyPlus name and its shape differs per family (see below).

The relations are attached too, so `AdjacencyCluster.GetResults<SpaceSimulationResult>(space)`
and `GetResults<SurfaceSimulationResult>(panel)` work directly. `Reference` is for the case
where you hold only the result list.

The EnergyPlus identity that `Reference` used to hold is still there, as parameters:

| Result | Parameters carrying the engine identity |
| --- | --- |
| `SpaceSimulationResult` | `ZoneIndex`, `ZoneName` |
| `SurfaceSimulationResult` | `SurfaceIndex`, and `HostSurfaceName` on a window/door |

An **unmatched** result keeps its raw SQL reference (a bare index) instead of a Guid, and is
named in a diagnostic — so a `Reference` that is not 32 hex characters means "this result did
not map to the model".

One panel represented by two engine surfaces (an internal wall, seen from both spaces) yields
two results sharing one `Reference`; tell them apart by `SurfaceIndex`. Values are never
summed across engine surfaces.

### Why one space returns four results

Not duplicates — two independent families, each with a heating and a cooling result:

| Family | `Name` looks like | Has parameter | Answers |
| --- | --- | --- | --- |
| **Annual** | the SAM space name, e.g. `Cell 1` | `Load` [W], `LoadIndex`, `UnmetHours` | what the zone actually peaked at across the weather year |
| **Design day** | the EnergyPlus zone name, e.g. `SAM_ThermalZone_Cell_1_<guid8>` | `DesignLoad` [W], `DesignDayName`, `PeakDate` | what plant would be sized at on the ASHRAE design day |

So a one-space model with sizing periods gives 2 + 2 = 4. Without sizing periods (no DDY and
no embedded design days) you get only the annual pair.

The two are **deliberately not merged** and their magnitudes legitimately differ — an annual
peak includes real weather and thermal dynamics, a design-day load is a steady-state sizing
calculation. All four reference the same `Space` Guid.

**To split them in Grasshopper**, filter on which parameter is present — `Load` for annual,
`DesignLoad` for design day — then on `LoadType` for heating vs cooling. The naming asymmetry
is historical (the design-day family is read straight from the SQL `ZoneSizes` table, which is
keyed by EnergyPlus zone name); `Reference` is uniform across both, which is why it is the key
to map on.

Full rules: [SAM_OPENSTUDIO_RESULT_MAPPING.md](SAM_OPENSTUDIO_RESULT_MAPPING.md).

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
