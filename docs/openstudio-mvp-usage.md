# SAM → OpenStudio MVP — usage

Companion to [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md)
(binding contract) and [openstudio-mvp-status.md](openstudio-mvp-status.md) (run status).

## Prerequisites

- OpenStudio CLI + EnergyPlus. Discovery order: explicit `OpenStudioRunOptions.CliPath` →
  `openstudio.exe` on PATH → direct installations (`C:\openstudio-*`, `%ProgramFiles%\OpenStudio*`)
  → Ladybug Tools bundle (`%ProgramFiles%\ladybug_tools\openstudio`). Verified against
  **3.10.0** (matching the NuGet SDK).
- An EPW weather file (explicit input — SAM weather data is not consulted in the MVP).
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
};
var runOptions = new SAM.Core.OpenStudio.OpenStudioRunOptions
{
    CliPath = @"C:\Program Files\ladybug_tools\openstudio",  // explicit CLI (exe or install dir)
    TimeoutSeconds = 3600,  // real timeout: the process tree (CLI + EnergyPlus) is killed on expiry
};
```

## Grasshopper components (SAM → OpenStudio tab)

| Component | Purpose |
| --- | --- |
| `SAMAnalytical.ToOpenStudio` | AnalyticalModel + EPW + output folder (+`_run`) → OSM/OSW paths, SQL path, heating/cooling kWh, diagnostics, success |
| `OpenStudio.RunModel` | Existing OSM/OSW (+EPW for OSM) → run, SQL path, heating/cooling kWh, diagnostics |
| `OpenStudio.CreateSpaceSimulationResultsBySQL` (pre-existing) | SQL → SAM `SpaceSimulationResult` objects |
| `OpenStudioCreateDesignDaysBySQL`, `SAMAnalyticalAddResultsBySQL` (pre-existing) | Design days / result attachment from SQL |

Components are thin wrappers — every conversion rule lives in the tested
`SAM.Analytical.OpenStudio` API.

## Documented MVP limitations

- Ideal Loads only (no detailed HVAC); EnergyPlus object defaults for the Ideal Loads system.
- Heating-only / cooling-only conditioning is not supported — both setpoint profiles are
  required (SAM-OS-HVAC-001 otherwise).
- Latent equipment gains, humidification/dehumidification setpoints: not converted (warned).
- Aperture frame layers: not converted (pane layers only).
- Face holes beyond apertures: external boundary only (warned).
- DDY design days: not imported (annual Ideal Loads runs need no sizing periods).
- Schedules are 365-day hourly `ScheduleFixedInterval`. Profiles longer than 8760 hours (leap
  years) are truncated to the first 8760 hours with a warning (documented non-leap policy);
  shorter flat profiles tile hour-for-hour at their own period exactly as SAM does natively.
- Day-composed (weekly) profiles follow the LadybugTools `ScheduleRuleset` convention
  (sub-profile 0 = Monday … 6 = Sunday) and are rotated onto the run calendar: the full-pipeline
  overload derives the 1-Jan day of week from the EPW, or set
  `OpenStudioConversionOptions.FirstDayOfWeek` explicitly. Weather-free conversions fall back to
  Monday-first.
- Glazing optical sides follow the EnergyPlus definition (Front = side opposite the zone):
  SAM `External*` → Front, `Internal*` → Back — deliberately different from the SAM_LadybugTools
  exporter (see `SAM_OPENSTUDIO_MATERIAL_MAPPING.md`).
- Self-intersecting polygons are rejected with a diagnostic (never auto-repaired).
- Building north/rotation not applied (site comes from the EPW).
- SpaceType names embed a deterministic content hash (`SAM_InternalCondition_<Name>_<hash8>`):
  internal conditions that share a name but differ in content intentionally get distinct
  SpaceTypes.
