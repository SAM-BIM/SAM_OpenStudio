# SAM → OpenStudio internal-condition and schedule mapping

**Status:** Binding for the MVP (M5). Units verified from SAM query sources
(`SAM.Analytical\Query\Calculated*.cs`, `OccupancyGain*.cs`) and the SAM_LadybugTools
`ProgramType.cs` reference — never inferred from parameter names.

## Deduplication and assignment

One OpenStudio `SpaceType` is created per unique `InternalCondition` identity — **sanitized name
plus a deterministic content hash** (`SAM_InternalCondition_<Name>_<hash8>`) — and assigned to
every space using that condition. `Space.InternalCondition`'s setter clones the condition with a
**new Guid** per space (`SAM.Analytical\Classes\Space.cs`), so Guids cannot identify shared
conditions. The content hash is computed over the condition's full parameter content (parameter
sets, profile-name references) with every nested `Guid` excluded: identical clones therefore
share one SpaceType, while same-named conditions with different gains, fractions or profile
references never merge silently (name-only deduplication was found and fixed in review P1-03).
Every per-space condition Guid is still registered in the traceability map against the SpaceType
name. Load densities are per-area/per-person normalized values derived through the established
SAM queries (evaluated for the first space encountered; SAM derives them from InternalCondition
parameters, so spaces sharing a condition share densities). Thermostat setpoints are **not**
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
| Equipment latent | `EquipmentLatentGain*` / `EquipmentLatentProfileName` | — | **not converted in MVP** | warning when present |
| Infiltration | `Analytical.Query.CalculatedInfiltrationAirFlow(space)` [m³/s] (from `InfiltrationAirChangesPerHour` [1/h] × `SpaceParameter.Volume` [m³]) ÷ Σ areas of sun-exposed panels (`AdjacencyCluster.ExposedToSun`) | `SpaceInfiltrationDesignFlowRate.FlowperExteriorSurfaceArea` [m³/s·m²] | LadybugTools formula parity | ≤ 0/NaN → no infiltration object |
| Infiltration schedule | `InfiltrationProfileName` | `SpaceInfiltrationDesignFlowRate.Schedule` | fraction schedule | flow without profile → **error** SAM-OS-SCH-001 |
| Outdoor air | `SpaceParameter.OutsideSupplyAirFlow` [m³/s] (absolute) | `DesignSpecificationOutdoorAir.OutdoorAirFlowRate` | direct | missing/≤0 → no DSOA (SAM has no per-person/per-area OA parameters; documented) |
| Heating/Cooling setpoints | `HeatingProfileName` / `CoolingProfileName` [°C] | thermostat schedules (M6) | temperature schedules | conditioned zone without setpoints → **error** SAM-OS-HVAC-001 (M6) |

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
heating/cooling; `SAM_ScheduleTypeLimits_ActivityLevel` for activity level. Leap years:
schedules are 365-day (EnergyPlus assumed non-leap year; TMYx weather is non-leap) —
documented MVP policy. Schedules are cached per Profile Guid; a missing profile is never
silently replaced (SAM-OS-SCH-001).
