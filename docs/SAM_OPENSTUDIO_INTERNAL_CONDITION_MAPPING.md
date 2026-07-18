# SAM → OpenStudio internal-condition and schedule mapping

**Status:** Binding for the MVP (M5). Units verified from SAM query sources
(`SAM.Analytical\Query\Calculated*.cs`, `OccupancyGain*.cs`) and the SAM_LadybugTools
`ProgramType.cs` reference — never inferred from parameter names.

## Deduplication and assignment

One OpenStudio `SpaceType` is created per unique `InternalCondition` **sanitized name** and
assigned to every space using that condition. Name-based (not Guid-based) deduplication is
deliberate and verified against SAM sources: `Space.InternalCondition`'s setter clones the
condition with a **new Guid** per space (`SAM.Analytical\Classes\Space.cs`), so Guids cannot
identify shared conditions — the same semantics SAM_LadybugTools uses (UniqueName-keyed
ProgramType). Every per-space condition Guid is still registered in the traceability map
against the shared SpaceType name. Load densities are per-area/per-person normalized values
derived through the established SAM queries (evaluated for the first space encountered; SAM
derives them from InternalCondition parameters, so spaces sharing a condition share
densities). Thermostat setpoints are **not** part of the SpaceType — they
are per-thermal-zone (M6). Space-specific overrides remain possible by assigning loads
directly to spaces in later work.

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

SAM `Profile` is an ordered value sequence (`Count`, indexer), optionally composed of
daily sub-profiles (`GetProfiles()`, weekly semantics as in SAM_LadybugTools
`ScheduleRuleset.cs`). MVP expansion to **8760 hourly values**:

1. `Count == 8760` → used as-is (annual profile).
2. `GetProfiles()` returns k ≥ 1 daily sub-profiles → extended to 7 by cycling (LadybugTools
   parity), each normalized to 24 hourly values, tiled Mon-first across 365 days
   (the run period sets Monday as the start day of week, M6).
3. Single profile whose `Count` divides 24 → each value held for 24/Count hours.
4. `Count` is a multiple of 24 (sub-hourly day, e.g. 48) → block-averaged to hourly.
5. Anything else → tiled cyclically across 8760 with a warning diagnostic.

Schedules are `ScheduleFixedInterval` (60-min interval, no timestep interpolation,
start 1 Jan). Type limits: `SAM_ScheduleTypeLimits_Fractional` (0–1, continuous,
dimensionless) for fraction profiles — values outside [0, 1] raise a warning, never
clamped; `SAM_ScheduleTypeLimits_Temperature` (continuous, temperature) for
heating/cooling; `SAM_ScheduleTypeLimits_ActivityLevel` for activity level. Leap years:
schedules are 365-day (EnergyPlus assumed non-leap year; TMYx weather is non-leap) —
documented MVP policy. Schedules are cached per Profile Guid; a missing profile is never
silently replaced (SAM-OS-SCH-001).
