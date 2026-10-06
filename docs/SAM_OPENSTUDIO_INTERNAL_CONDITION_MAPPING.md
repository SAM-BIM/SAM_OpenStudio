# SAM → OpenStudio internal-condition and schedule mapping

**Status:** Binding for the MVP (M5), extended in C2 (latent equipment, native ACH infiltration,
SpaceType outdoor air with ventilation schedule, humidistat, single-mode thermostats). Units
verified from SAM query sources (`SAM.Analytical\Query\Calculated*.cs`, `OccupancyGain*.cs`) and
the SAM_LadybugTools `ProgramType.cs` reference — never inferred from parameter names.

## Deduplication and assignment

One OpenStudio `SpaceType` is created per unique `InternalCondition` identity — **sanitized name
plus a deterministic content hash** (`SAM_InternalCondition_<Name>_<hash8>`) — and assigned to
every space using that condition. `Space.InternalCondition`'s setter clones the condition with a
**new Guid** per space (`SAM.Analytical\Classes\Space.cs`), so Guids cannot identify shared
conditions. The content hash is computed over the condition's full parameter content (parameter
sets, profile-name references) with every nested `Guid` excluded, **extended in C1 with the
per-space computed load densities** (people/area, lighting and equipment W/m², occupancy and
activity level, infiltration flow, area): identical clones evaluated for equivalent spaces
therefore share one SpaceType, while same-named conditions with different gains, fractions or
profile references — or a shared condition evaluated for spaces with different densities —
never merge silently (name-only deduplication was found and fixed in review P1-03; the
first-space-density imposition in review P3-02). Every per-space condition Guid is still
registered in the traceability map against the SpaceType name. Thermostat setpoints are **not**
part of the SpaceType — they are per-thermal-zone (M6). Space-specific overrides remain possible
by assigning loads directly to spaces in later work.

## Loads

| Load | SAM source (unit) | OpenStudio object/field | Conversion | Missing-value policy |
| --- | --- | --- | --- | --- |
| People density | `Analytical.Query.CalculatedPeoplePerArea(space)` [people/m²] (from `InternalConditionParameter.AreaPerPerson` [m²/person] or `SpaceParameter.Occupancy` + `SpaceParameter.Area`) | `PeopleDefinition.PeopleperSpaceFloorArea` | direct | density ≤ 0 or NaN → no People objects (no diagnostic; genuinely unoccupied) |
| Occupancy schedule | `InternalConditionParameter.OccupancyProfileName` → `ProfileLibrary` | `People.NumberofPeopleSchedule` | fraction schedule | gains present but profile missing → **error** SAM-OS-SCH-001; never AlwaysOn |
| Activity level | `Analytical.Query.OccupancyGain(space)` / `CalculatedOccupancy(space)` [W/person] | `People.ActivityLevelSchedule` | constant annual schedule (ActivityLevel type limits) | NaN → 0 with warning |
| People radiant fraction | `InternalConditionParameter.OccupancyRadiantProportion` [0–1] | `PeopleDefinition.FractionRadiant` | direct | missing → 0.3 (LadybugTools parity, warning-free documented default) |
| People sensible fraction | `OccupancySensibleGainPerPerson` and `OccupancyLatentGainPerPerson` [W/person] | `PeopleDefinition.SensibleHeatFraction` | sensible/(sensible+latent) | either missing → EnergyPlus autocalculate |
| Lighting density | `Analytical.Query.CalculatedLightingGain(space)` [W] ÷ `SpaceParameter.Area` [m²] | `LightsDefinition.WattsperSpaceFloorArea` | direct [W/m²] | ≤ 0/NaN → no Lights objects |
| Lighting schedule | `LightingProfileName` | `Lights.Schedule` | fraction schedule | gains without profile → **error** SAM-OS-SCH-001 |
| Lighting radiant fraction | `LightingRadiantProportion` | `LightsDefinition.FractionRadiant` | direct | missing → 0.32 (LadybugTools parity) |
| Lighting visible fraction | `LightingViewCoefficient` | `LightsDefinition.FractionVisible` | direct | missing → 0.25 (LadybugTools parity) |
| Equipment density | `Analytical.Query.CalculatedEquipmentSensibleGain(space)` [W] ÷ area | `ElectricEquipmentDefinition.WattsperSpaceFloorArea` | direct [W/m²] | ≤ 0/NaN → no equipment objects |
| Equipment schedule | `EquipmentSensibleProfileName` | `ElectricEquipment.Schedule` | fraction schedule | gains without profile → **error** SAM-OS-SCH-001 |
| Equipment radiant fraction | `EquipmentRadiantProportion` | `ElectricEquipmentDefinition.FractionRadiant` | direct | missing → 0 (LadybugTools parity) |
| Equipment latent | `Analytical.Query.CalculatedEquipmentLatentGain(space)` [W] ÷ area; `EquipmentLatentProfileName` | dedicated `ElectricEquipment` instance, `FractionLatent = 1`, `FractionRadiant = 0` | separate instance so sensible/latent keep independent profiles (C2) | ≤ 0/NaN → no latent instance; gains without profile → **error** SAM-OS-SCH-001 |
| Infiltration (ACH, C2) | `InternalConditionParameter.InfiltrationAirChangesPerHour` [1/h] | `SpaceInfiltrationDesignFlowRate.AirChangesperHour` | direct | ACH present → native basis; ≤ 0/NaN → fallback below |
| Infiltration (fallback) | `Analytical.Query.CalculatedInfiltrationAirFlow(space)` [m³/s] ÷ Σ areas of sun-exposed panels (`AdjacencyCluster.ExposedToSun`) | `SpaceInfiltrationDesignFlowRate.FlowperExteriorSurfaceArea` [m³/s·m²] | LadybugTools formula parity | ≤ 0/NaN → no infiltration object |
| Infiltration schedule | `InfiltrationProfileName` | `SpaceInfiltrationDesignFlowRate.Schedule` | fraction schedule | flow without profile → **error** SAM-OS-SCH-001 |
| Outdoor air (C2) | `SupplyAirFlowPerPerson` [m³/s·p], `SupplyAirFlowPerArea` [m³/s·m²], `SupplyAirChangesPerHour` [1/h], `SupplyAirFlow` [m³/s] | SpaceType `DesignSpecificationOutdoorAir`, method **Sum** | direct | all ≤ 0/NaN → no SpaceType DSOA |
| Outdoor-air schedule (C2) | `VentilationProfileName` | `DesignSpecificationOutdoorAir.OutdoorAirFlowRateFractionSchedule` | fraction schedule | named but unresolved → **error** SAM-OS-SCH-001; unnamed → unscheduled (fraction 1) |
| Outdoor air (space override) | `SpaceParameter.OutsideSupplyAirFlow` [m³/s] (absolute) | per-space `DesignSpecificationOutdoorAir.OutdoorAirFlowRate` | direct; takes precedence over the SpaceType DSOA (EnergyPlus space-over-type rule) | missing/≤0 → SpaceType DSOA governs |
| Humidity control (C2) | `HumidificationProfileName` / `DehumidificationProfileName` [%RH] | `ZoneControlHumidistat` humidifying/dehumidifying schedules (Percent limits 0–100) + Ideal Loads `Humidification/DehumidificationControlType = Humidistat` when the matching schedule exists | profile → schedule; SAM carries setpoint *profiles*, not numeric setpoints | named but unresolved → **error** SAM-OS-SCH-001; absent → no humidity control (EnergyPlus defaults) |
| Pollutants | `PollutantGenerationPerPerson/PerArea`, `PollutantProfileName` | — | **unsupported** (generic contaminant modelling out of scope) | warning SAM-OS-IC-001 when present |
| Heating/Cooling setpoints | `HeatingProfileName` / `CoolingProfileName` [°C] | thermostat schedules (M6); single-mode when only one profile exists (C2 — EnergyPlus SingleHeating/SingleCooling, no invented setpoints) | temperature schedules | both missing on a conditioned zone → **error** SAM-OS-HVAC-001 |

## Profile → ScheduleFixedInterval (MVP schedule policy)

SAM `Profile` is an indexed value sequence (`Min`/`Max`, wrapping indexer), optionally composed of
daily sub-profiles (`GetProfiles()`, weekly semantics as in SAM_LadybugTools
`ScheduleRuleset.cs`). MVP expansion to **8760 hourly values** follows SAM's own expansion
semantics (`Profile.GetYearlyValues()` — cyclic tiling through the wrapping indexer):

1. `Count` span == 8760 → used as-is (annual profile).
2. Span > 8760 (e.g. 8784 leap-year) → the first 8760 hours are used with a **warning**
   (365-day non-leap schedule policy); the year is never averaged into one day.
3. `GetProfiles()` returns k ≥ 1 daily sub-profiles → extended to 7 by cycling (LadybugTools
   parity), each expanded to 24 hourly values through the SAM indexer, tiled across 365 days
   **rotated onto the run calendar**: sub-profile 0 is Monday per the LadybugTools
   `ScheduleRuleset` convention, so the week is shifted by the 1-Jan day of week of the run —
   derived from the EPW (`EpwFile.startDayOfWeek`, e.g. Sunday for the pinned Boston TMYx) in
   the full-pipeline overload, or from `OpenStudioConversionOptions.FirstDayOfWeek` when set
   explicitly; Monday-first is the fallback for weather-free conversions.
4. Any other flat profile → tiled hour-for-hour at its own period through the SAM indexer —
   a 24-hour day repeats daily, a 168-hour week repeats weekly; values are never stretched,
   held or block-averaged.
5. Gaps (indexes without values) are an **error** (`SAM-OS-SCH-001`) — NaN never reaches a
   schedule; a missing profile is never silently replaced.

Schedules are `ScheduleFixedInterval` (60-min interval, no timestep interpolation,
start 1 Jan). Type limits: `SAM_ScheduleTypeLimits_Fractional` (0–1, continuous,
dimensionless) for fraction profiles — values outside [0, 1] raise a warning, never
clamped; `SAM_ScheduleTypeLimits_Temperature` (continuous, temperature) for
heating/cooling; `SAM_ScheduleTypeLimits_Percent` (0–100, percent) for
humidification/dehumidification (C2) — values outside [0, 100] raise a warning, never
clamped; `SAM_ScheduleTypeLimits_ActivityLevel` for activity level. Leap years:
schedules are 365-day (EnergyPlus assumed non-leap year; TMYx weather is non-leap) —
documented MVP policy (leap-year calendar wiring arrives in C4). Schedules are cached per
(Profile Guid, ProfileType) — the same profile reused under a different type yields a
separate schedule with the correct type limits (C1); a missing profile is never
silently replaced (SAM-OS-SCH-001).
