# SAM → OpenStudio result mapping

**Status:** Binding for C5. Covers extraction from the EnergyPlus SQLite output into the
engine-neutral `OpenStudioSimulationResultSet` and the mapping into SAM result classes.

## Unit authority

One converter family in `SAM.Core.OpenStudio.Query.ConvertUnit`:

| Helper | Conversion | Used for |
| --- | --- | --- |
| `JoulesToKilowattHours` | J ÷ 3 600 000 → kWh | all annual energies |
| `JoulesPerIntervalToWatts` | J ÷ interval seconds → W | peaks (hour-average power) and at-peak gains |
| `ConvertUnit(DataTable, …)` | ReportDataDictionary.Units authority ("J" → `JoulesPerIntervalToWatts`) | the legacy SpaceSimulationResult DataTable path |

No extraction path divides by a local constant. The historical divergence (÷3.6e6 vs ÷3600)
is resolved: ÷3.6e6 is kWh (annual), ÷3600 is hour-average watts (at-peak); both route through
the helpers above.

## Zone identity

EnergyPlus report keys vary by variable class: Ideal Loads variables key on the **system
name**, zone-level variables on the **ThermalZone name**, enclosure variables on the **Space
name**. All three carry the deterministic SAM Guid suffix (first 8 hex digits of the space
Guid), so extraction normalises every per-zone dictionary onto the Ideal Loads system key —
the result set has ONE zone identity, matched back to SAM spaces through
`Core.OpenStudio.Query.OpenStudioName` (case-insensitively).

## Extraction (SQL, parameterised, weather-run environment only)

All queries use parameterised `System.Data.SQLite` commands and are restricted to the
weather-run environment (`EnvironmentPeriods.EnvironmentType = 3`) — imported design days
never double-count into annual totals or peaks.

| Result-set member | EnergyPlus source | Conversion |
| --- | --- | --- |
| AnnualHeating/CoolingEnergy | `Zone Ideal Loads Supply Air Total Heating/Cooling Energy` [J], SUM | J → kWh |
| PeakHeating/CoolingLoad + Hour | same variables, hourly series max | hour-average W → kW; hour-of-year from the Time table |
| PeakHeating/CoolingLoadTotal + HourTotal | hourly series summed across zones (coincident) | hour-average W → kW |
| UnmetHeating/CoolingHours | `Zone Heating/Cooling Setpoint Not Met Time` [h], SUM | none |
| PeopleGains | `Zone People Total Heating Energy` [J], SUM | J → kWh |
| LightingGains | `Zone Lights Total Heating Energy` [J], SUM | J → kWh |
| EquipmentGains | `Zone Electric Equipment Total Heating Energy` [J], SUM | J → kWh |
| WindowSolarGains | `Enclosure Windows Total Transmitted Solar Radiation Energy` [J], SUM (Enclosure-scoped in current EnergyPlus; the `Zone Windows …` name was retired) | J → kWh |
| InfiltrationGains | `Zone Infiltration Sensible Heat Gain/Loss Energy` [J], net (gain − loss) | J → kWh |
| VentilationHeating/CoolingEnergy | `Zone Ideal Loads Outdoor Air Sensible Heating/Cooling Energy` [J], SUM | J → kWh |
| TemperatureSeries / OperativeTemperatureSeries / RelativeHumiditySeries | `Zone Mean Air Temperature` / `Zone Operative Temperature` / `Zone Air Relative Humidity`, hourly | none; only when `OpenStudioRunOptions.ExtractTimeSeries` |
| RuntimeSeconds / WarningCount / SevereCount / FatalCount | CLI wall-clock; eplusout.err counts | none |

Zero-vs-missing: a zone absent from the report (unconditioned, no Ideal Loads) is absent from
every dictionary — never zero-filled. A variable with no rows for a key produces no entry.

## SAM mapping (`Convert/ToSAM/SimulationResults.cs`)

| SAM slot | Source | Notes |
| --- | --- | --- |
| `AnalyticalModelSimulationResultParameter.ConsumptionHeating/Cooling` [kWh] | TotalAnnualHeating/Cooling | |
| `.PeakHeating/CoolingLoad` [kW] | coincident totals | |
| `.PeakHeating/CoolingHour` [h] | hour-of-year of the coincident peak | |
| `.FloorArea` / `.Volume` [m²/m³] | Σ `SpaceParameter.Area` / `.Volume` | Derived |
| `SpaceSimulationResultParameter.Load` [W] | per-zone peak load × 1000 | one result per LoadType (Heating/Cooling), SAM pattern |
| `.LoadIndex` [h] | hour-of-year of the zone peak | |
| `.UnmetHours` [h] | per-zone not-met hours | |

At-peak gain slots (solar, lighting, equipment, occupancy, infiltration W-values) and surface
results stay with the established design-day `ZoneSizes` SQLite path
(`Create/SpaceSimulationResults.cs`, `Create/SurfaceSimulationResults.cs`), which requires
sizing runs (C4 DDY import). Time series do not fit SAM parameter bags; they live in the
result set only.

## `SAMAnalytical.AddResultsBySQL` attachment rules (human-Rhino fix 3)

`Modify.AddResults(AdjacencyCluster, path)` now combines both families: the annual
engine-neutral result set above (per-LoadType `SpaceSimulationResult` with Load [W],
LoadIndex, UnmetHours) and the design-day `ZoneSizes` family (DesignLoad) when sizing ran.

- **Zone/panel resolution**: EnergyPlus names (`SAM_<type>_<name>_<guid8>`) are matched to SAM
  objects by their deterministic 8-hex Guid suffix, then by full-Guid reference, then by
  sanitized name — never by display name alone. Unmatched SQL zones/surfaces and SAM
  spaces/panels with no result are both reported as structured diagnostics (a reported zero
  stays a valid result; only absence is diagnosed).
- **Surface aggregation rule**: one `SurfaceSimulationResult` per engine surface (identity:
  SQL `SurfaceIndex` in `Reference`). An internal panel represented by two engine surfaces
  receives two results related to the same panel — values are never summed across surfaces.
  Surface values available without sizing runs: area, zone identity, panel linkage; inside/
  outside conduction at the space peak is added per load type when `ZoneSizes` data exists.
- **Subsurfaces**: the EnergyPlus `Surfaces` table lists subsurfaces alongside base surfaces,
  and a `SAM_SubSurface_<name>_<guid8>` name carries the **Aperture** Guid, never a panel Guid.
  Apertures are not standalone `AdjacencyCluster` objects, so a window result is related to the
  panel **hosting** that aperture, keeping its own SQL name and `SurfaceIndex` reference. A
  glazed panel therefore carries one result for its opaque surface plus one per hosted aperture.
- **Rerun**: identical results (type, name, reference, load type) already present from the
  same source are not duplicated; the space/panel relation is ensured.
- The Grasshopper output name `panelSimulationResults` is retained for compatibility; the SAM
  class is `SurfaceSimulationResult`. The component clones the input before attaching results
  (non-mutating convention).
