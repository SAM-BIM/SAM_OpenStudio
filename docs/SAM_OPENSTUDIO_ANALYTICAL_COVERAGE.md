# SAM → OpenStudio Analytical Translation Coverage

Status: reviewed 2026-07-19 (programme *SAM → OpenStudio Analytical Completeness v1*, milestone C0).

This document is the complete audit of the `SAM.Analytical.AnalyticalModel` data surface
against the native OpenStudio translation in this repository. It is **generated from and kept
in lockstep with** the machine-readable manifest
[`tests/resources/openstudio-analytical-coverage.json`](../tests/resources/openstudio-analytical-coverage.json);
the completeness tests enforce the manifest (never this Markdown). When coverage changes, update
the manifest first and regenerate the tables here.

## Classification scheme

Every SAM property or concept with potential energy-model semantics is classified as exactly one of:

| Status | Meaning |
|---|---|
| **Native** | Direct OpenStudio/EnergyPlus representation; values pass through (unit-converted where needed). |
| **Derived** | Deterministically calculated from SAM source data (formula documented per row). |
| **Approximated** | Mapped under a documented engineering assumption; a structured diagnostic notes the approximation where it can distort results. |
| **Unsupported** | No safe OpenStudio equivalent; when source data is present a structured diagnostic is emitted. Nothing is silently dropped. |
| **Deferred** | Belongs to the detailed-HVAC programme (air/plant loops, equipment, controls, water systems). Ideal Loads remains the simulation system here. |
| **NA** | No translation applies: display/compliance metadata, result-only slots, derived classifications OpenStudio computes itself, or concepts with no SAM source data. |

The **Milestone** column names where the final mapping lands: `MVP` = merged PR #7;
`C1`–`C5` = this programme's milestones; `C0` marks rows whose classification (NA/Deferred)
*is* the outcome. Rows are updated as milestones complete.

## Explicitly deferred domains

Detailed SAM AirSystem conversion; air loops and plant loops; heating/cooling system equipment;
water systems; control sequences; Modelica/FMI; compliance workflows (NCM, Part F, Part O);
the TAS-versus-EnergyPlus validation harness; AI-assisted QA.

## Reclassifications agreed at programme approval

- **Glazing dividers/muntins — NA**: SAM has no divider geometry model (`AperturePart` is Pane|Frame only).
- **Aperture blinds/shades (`IsBlind`, `FeatureShade`) — Unsupported** with diagnostic: SAM carries no slat/shade geometry, so fabricating `WindowMaterial:Blind` inputs would be invention.
- **Opening properties (openable fraction, discharge coefficient) — Unsupported** under Ideal Loads (AirflowNetwork territory), deferred to the HVAC programme.
- **STAT-file ground temperatures — deferred**: no STAT parser exists in the SAM ecosystem; SAM `WeatherData` and the EPW header cover the need.
- **SAM hourly `DesignDay` → parametric `SizingPeriod:DesignDay` — Approximated** (documented fit), downgradeable to a diagnostic if unsafe; deterministic DDY import is the primary design-day path.
- **Subhourly profiles — NA**: SAM `Profile` stores are hour-indexed; no subhourly source data exists.
- **Daylight saving — Approximated, default off** (energy-model convention); SAM carries no DST data, the EPW header is the only source.
- **Emitter radiant proportions — Deferred**: Ideal Loads is a purely convective air system.
- **Pollutant profiles — Unsupported** (EnergyPlus generic-contaminant modelling out of scope).

## Coverage tables

Legend: **Status** per the scheme above; **Conversion / policy** carries the formula,
diagnostic code and/or note recorded in the manifest.

### Internal conditions (`SAM.Analytical.InternalConditionParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `InternalConditionParameter.Color` | - | SpaceType RenderingColor | Native | C2 | Display colour only; no energy effect. |
| `InternalConditionParameter.AreaPerPerson` | m2/p | OS:People:Definition People per Space Floor Area | Native | MVP | peoplePerArea = Analytical.Query.CalculatedPeoplePerArea(space) |
| `InternalConditionParameter.OccupancyProfileName` | - | OS:People Number of People Schedule (ScheduleFixedInterval) | Native | MVP | SAM-OS-SCH-001 error when gains exist but profile unresolved; load skipped |
| `InternalConditionParameter.OccupancySensibleGainPerPerson` | W/p | OS:People:Definition Sensible Heat Fraction + constant ActivityLevel schedule | Derived | MVP | sensibleFraction = S/(S+L); activityLevel = OccupancyGain/CalculatedOccupancy [W/p] |
| `InternalConditionParameter.OccupancyLatentGainPerPerson` | W/p | OS:People:Definition Sensible Heat Fraction (complement) + ActivityLevel schedule | Derived | MVP | latent = activityLevel * (1 - sensibleFraction) |
| `InternalConditionParameter.EquipmentSensibleProfileName` | - | OS:ElectricEquipment Schedule (sensible instance) | Native | MVP | SAM-OS-SCH-001 error when gains exist but profile unresolved |
| `InternalConditionParameter.EquipmentSensibleGain` | W | OS:ElectricEquipment:Definition Watts per Space Floor Area (sensible instance) | Derived | MVP | W/m2 = CalculatedEquipmentSensibleGain(space)/SpaceParameter.Area |
| `InternalConditionParameter.EquipmentSensibleGainPerArea` | W/m2 | OS:ElectricEquipment:Definition Watts per Space Floor Area (sensible instance) | Native | MVP | - |
| `InternalConditionParameter.EquipmentSensibleGainPerPerson` | W/p | OS:ElectricEquipment:Definition Watts per Space Floor Area (sensible instance) | Derived | MVP | folded through Analytical.Query.CalculatedEquipmentSensibleGain(space) |
| `InternalConditionParameter.EquipmentLatentProfileName` | - | OS:ElectricEquipment Schedule (dedicated latent instance) | Native | C2 | SAM-OS-SCH-001 error when latent gains exist but profile unresolved |
| `InternalConditionParameter.EquipmentLatentGain` | W | OS:ElectricEquipment:Definition (latent instance, Fraction Latent = 1) | Derived | C2 | W/m2 = CalculatedEquipmentLatentGain(space)/SpaceParameter.Area; separate instance so sensible/latent keep independent profiles |
| `InternalConditionParameter.EquipmentLatentGainPerArea` | W/m2 | OS:ElectricEquipment:Definition (latent instance, Fraction Latent = 1) | Native | C2 | - |
| `InternalConditionParameter.LightingGainPerArea` | W/m2 | OS:Lights:Definition Watts per Space Floor Area | Native | MVP | - |
| `InternalConditionParameter.LightingGainPerPerson` | W/p | OS:Lights:Definition Watts per Space Floor Area | Derived | MVP | folded through Analytical.Query.CalculatedLightingGain(space) |
| `InternalConditionParameter.LightingGain` | W | OS:Lights:Definition Watts per Space Floor Area | Derived | MVP | W/m2 = CalculatedLightingGain(space)/SpaceParameter.Area |
| `InternalConditionParameter.LightingLevel` | lux | OS:Lights:Definition Watts per Space Floor Area | Derived | MVP | lux-based lighting gain folded through Analytical.Query.CalculatedLightingGain (with LightingEfficiency) |
| `InternalConditionParameter.LightingProfileName` | - | OS:Lights Schedule | Native | MVP | SAM-OS-SCH-001 error when gains exist but profile unresolved |
| `InternalConditionParameter.LightingEfficiency` | W/m2/100lux | OS:Lights:Definition Watts per Space Floor Area | Derived | MVP | with LightingLevel through CalculatedLightingGain |
| `InternalConditionParameter.InfiltrationAirChangesPerHour` | ACH | OS:SpaceInfiltration:DesignFlowRate Air Changes per Hour | Native | C2 | MVP approximated as Flow per Exterior Surface Area; C2 uses the native ACH field. |
| `InternalConditionParameter.InfiltrationProfileName` | - | OS:SpaceInfiltration:DesignFlowRate Schedule | Native | MVP | SAM-OS-SCH-001 error when infiltration exists but profile unresolved |
| `InternalConditionParameter.PollutantGenerationPerPerson` | g/h/p | - | Unsupported | C2 | SAM-OS-IC-001 warning; EnergyPlus generic contaminant modelling out of scope |
| `InternalConditionParameter.PollutantGenerationPerArea` | g/h/m2 | - | Unsupported | C2 | SAM-OS-IC-001 warning |
| `InternalConditionParameter.PollutantProfileName` | - | - | Unsupported | C2 | SAM-OS-IC-001 warning |
| `InternalConditionParameter.HeatingEmitterRadiantProportion` | 0-1 | - | Deferred | C2 | SAM-OS-HVAC-001 info; Ideal Loads is a convective air system - emitter characteristics need detailed HVAC |
| `InternalConditionParameter.HeatingEmitterCoefficient` | 0-1 | - | Deferred | C2 | SAM-OS-HVAC-001 info |
| `InternalConditionParameter.HeatingProfileName` | - | OS:ThermostatSetpoint:DualSetpoint Heating Setpoint Schedule | Native | MVP | - |
| `InternalConditionParameter.CoolingEmitterRadiantProportion` | 0-1 | - | Deferred | C2 | SAM-OS-HVAC-001 info |
| `InternalConditionParameter.CoolingEmitterCoefficient` | 0-1 | - | Deferred | C2 | SAM-OS-HVAC-001 info |
| `InternalConditionParameter.CoolingProfileName` | - | OS:ThermostatSetpoint:DualSetpoint Cooling Setpoint Schedule | Native | MVP | - |
| `InternalConditionParameter.HumidificationProfileName` | - | OS:ZoneControl:Humidistat Humidifying Relative Humidity Schedule + IdealLoads Humidification Control Type Humidistat | Native | C2 | SAM-OS-SCH-001 error when named but unresolved |
| `InternalConditionParameter.DehumidificationProfileName` | - | OS:ZoneControl:Humidistat Dehumidifying Relative Humidity Schedule + IdealLoads Dehumidification Control Type Humidistat | Native | C2 | SAM-OS-SCH-001 error when named but unresolved |
| `InternalConditionParameter.VentilationSystemTypeName` | - | - | Deferred | C2 | System identity feeds conditioned-space classification (Query.IsConditioned); equipment itself is detailed HVAC. |
| `InternalConditionParameter.CoolingSystemTypeName` | - | - | Deferred | C2 | Informs IsConditioned classification. |
| `InternalConditionParameter.HeatingSystemTypeName` | - | - | Deferred | C2 | Informs IsConditioned classification. |
| `InternalConditionParameter.SupplyAirFlowPerPerson` | m3/s/p | OS:DesignSpecification:OutdoorAir Outdoor Air Flow per Person (method Sum) | Native | C2 | - |
| `InternalConditionParameter.ExhaustAirFlowPerPerson` | m3/s/p | - | Deferred | C2 | SAM-OS-HVAC-001 info; exhaust flows need fan/air-loop equipment |
| `InternalConditionParameter.SupplyAirChangesPerHour` | ACH | OS:DesignSpecification:OutdoorAir Air Changes per Hour (method Sum) | Native | C2 | - |
| `InternalConditionParameter.ExhaustAirChangesPerHour` | ACH | - | Deferred | C2 | SAM-OS-HVAC-001 info |
| `InternalConditionParameter.SupplyAirFlowPerArea` | m3/s/m2 | OS:DesignSpecification:OutdoorAir Outdoor Air Flow per Floor Area (method Sum) | Native | C2 | - |
| `InternalConditionParameter.ExhaustAirFlowPerArea` | m3/s/m2 | - | Deferred | C2 | SAM-OS-HVAC-001 info |
| `InternalConditionParameter.SupplyAirFlow` | m3/s | OS:DesignSpecification:OutdoorAir Outdoor Air Flow Rate (method Sum) | Native | C2 | SpaceParameter.OutsideSupplyAirFlow takes precedence when present (space override). |
| `InternalConditionParameter.ExhaustAirFlow` | m3/s | - | Deferred | C2 | SAM-OS-HVAC-001 info |
| `InternalConditionParameter.VentilationProfileName` | - | OS:DesignSpecification:OutdoorAir Outdoor Air Schedule | Native | C2 | SAM-OS-SCH-001 error when named but unresolved |
| `InternalConditionParameter.LightingRadiantProportion` | 0-1 | OS:Lights:Definition Fraction Radiant | Native | MVP | Documented default 0.32 when absent. |
| `InternalConditionParameter.OccupancyRadiantProportion` | 0-1 | OS:People:Definition Fraction Radiant | Native | MVP | Documented default 0.3 when absent. |
| `InternalConditionParameter.EquipmentRadiantProportion` | 0-1 | OS:ElectricEquipment:Definition Fraction Radiant | Native | MVP | Documented default 0 when absent. |
| `InternalConditionParameter.LightingViewCoefficient` | 0-1 | OS:Lights:Definition Fraction Visible | Approximated | MVP | TAS view coefficient mapped to EnergyPlus visible fraction; documented default 0.25 when absent. |
| `InternalConditionParameter.OccupancyViewCoefficient` | 0-1 | - | Unsupported | C2 | SAM-OS-IC-001 info; OS:People has no visible-fraction field |
| `InternalConditionParameter.EquipmentViewCoefficient` | 0-1 | - | Unsupported | C2 | SAM-OS-IC-001 info; OS:ElectricEquipment has no visible-fraction field |
| `InternalConditionParameter.LightingControlFunction` | - | - | Unsupported | C2 | SAM-OS-IC-001 info; TAS control function string has no deterministic daylighting-control mapping |
| `InternalConditionParameter.VentilationFunction` | - | - | Unsupported | C2 | SAM-OS-IC-001 info; TAS function expression |
| `InternalConditionParameter.VentilationFunctionFactor` | - | - | Unsupported | C2 | SAM-OS-IC-001 info |
| `InternalConditionParameter.VentilationFunctionSetback` | - | - | Unsupported | C2 | SAM-OS-IC-001 info |
| `InternalConditionParameter.VentilationFunctionDescription` | - | - | NA | C0 | Documentation string. |
| `InternalConditionParameter.NCMData` | - | - | NA | C0 | UK NCM compliance metadata; no thermal semantics. |
| `InternalConditionParameter.Description` | - | - | NA | C0 | Metadata. |

### Space parameters (`SAM.Analytical.SpaceParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `SpaceParameter.Color` | - | - | NA | C0 | Display colour; SpaceType rendering colour covers presentation. |
| `SpaceParameter.DesignHeatingLoad` | W | - | NA | C0 | Result slot written back by C5 result mapping, not a translation input. |
| `SpaceParameter.DesignCoolingLoad` | W | - | NA | C0 | Result slot written back by C5 result mapping. |
| `SpaceParameter.Volume` | m3 | load derivations (ACH -> flow); OpenStudio computes zone volume from geometry | Derived | MVP | - |
| `SpaceParameter.Area` | m2 | density denominators for W and W/p bases | Derived | MVP | - |
| `SpaceParameter.Occupancy` | p | OS:People:Definition People per Space Floor Area | Derived | MVP | peoplePerArea = CalculatedOccupancy/Area (space override wins over AreaPerPerson) |
| `SpaceParameter.FacingExternal` | - | - | NA | C0 | Derived classification; OpenStudio derives exposure from geometry and boundary conditions. |
| `SpaceParameter.FacingExternalGlazing` | - | - | NA | C0 | Derived classification. |
| `SpaceParameter.LevelName` | - | OS:BuildingStory assignment/naming fallback | Derived | C1 | Used by the story fallback when no floor-group panels exist. |
| `SpaceParameter.CoolingSizingFactor` | - | OS:Sizing:Zone Cooling Sizing Factor | Native | C4 | Effective only when sizing runs are enabled. |
| `SpaceParameter.HeatingSizingFactor` | - | OS:Sizing:Zone Heating Sizing Factor | Native | C4 | - |
| `SpaceParameter.VentilationRiserName` | - | - | Deferred | C0 | Distribution identity; detailed HVAC. |
| `SpaceParameter.HeatingRiserName` | - | - | Deferred | C0 | - |
| `SpaceParameter.CoolingRiserName` | - | - | Deferred | C0 | - |
| `SpaceParameter.OutsideSupplyAirFlow` | m3/s | OS:DesignSpecification:OutdoorAir Outdoor Air Flow Rate (per-space) | Native | MVP | Space-level override; takes precedence over InternalCondition supply values (C2). |
| `SpaceParameter.SupplyAirFlow` | m3/s | - | Deferred | C0 | Total supply air is system equipment; Ideal Loads sizes its own supply. |
| `SpaceParameter.ExhaustAirFlow` | m3/s | - | Deferred | C0 | - |
| `SpaceParameter.DaylightFactor` | - | - | NA | C0 | Result-style metric; no translation input. |
| `SpaceParameter.PartFSpaceData` | - | - | NA | C0 | UK Part F compliance metadata. |

### Profile types (`SAM.Analytical.ProfileType`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `ProfileType.Undefined` | - | - | NA | C0 | Never bound to a load slot; unresolved profile references raise SAM-OS-SCH-001. |
| `ProfileType.Occupancy` | fraction | ScheduleFixedInterval (Fractional limits) | Native | MVP | - |
| `ProfileType.EquipmentSensible` | fraction | ScheduleFixedInterval (Fractional limits) | Native | MVP | - |
| `ProfileType.EquipmentLatent` | fraction | ScheduleFixedInterval (Fractional limits) on latent equipment instance | Native | C2 | - |
| `ProfileType.Lighting` | fraction | ScheduleFixedInterval (Fractional limits) | Native | MVP | - |
| `ProfileType.Infiltration` | fraction | ScheduleFixedInterval (Fractional limits) | Native | MVP | - |
| `ProfileType.Pollutant` | - | - | Unsupported | C2 | SAM-OS-IC-001 warning when referenced |
| `ProfileType.Heating` | C | ScheduleFixedInterval (Temperature limits) | Native | MVP | - |
| `ProfileType.Cooling` | C | ScheduleFixedInterval (Temperature limits) | Native | MVP | - |
| `ProfileType.Humidification` | %RH | ScheduleFixedInterval (Percent limits) on ZoneControl:Humidistat | Native | C2 | - |
| `ProfileType.Dehumidification` | %RH | ScheduleFixedInterval (Percent limits) on ZoneControl:Humidistat | Native | C2 | - |
| `ProfileType.Ventilation` | fraction | ScheduleFixedInterval (Fractional limits) on DesignSpecification:OutdoorAir | Native | C2 | - |
| `ProfileType.Other` | - | - | NA | C0 | No load slot binds ProfileType.Other. |

### Profile semantics (`SAM.Analytical.Profile`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Profile.HourlyAnnualValues` | - | ScheduleFixedInterval, 8760 hourly values | Native | MVP | - |
| `Profile.WeeklyDayComposition` | - | ScheduleFixedInterval via 7 day-vectors rotated by run-calendar first day of week | Native | MVP | - |
| `Profile.ValuesBeyond8760` | - | first 8760 values used on non-leap run calendars | Approximated | MVP | SAM-OS-SCH-001 warning (documented truncation policy) |
| `Profile.LeapYear8784` | - | ScheduleFixedInterval, 8784 values when the run calendar is a leap year | Derived | C4 | explicit >=8784 stores pass through; day-composed/cyclic profiles tile to 8784 |
| `Profile.SubhourlyValues` | - | - | NA | C0 | SAM Profile stores are hour-indexed (SortedList<int,...>); no subhourly source data exists. |
| `Profile.FractionOutOfRange` | - | kept as-authored | Native | MVP | SAM-OS-SCH-001 warning; values never clamped |
| `Profile.MissingOrNaNValues` | - | - | Native | MVP | SAM-OS-SCH-001 error; schedule not created, never substituted |

### Aperture parameters (`SAM.Analytical.ApertureParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `ApertureParameter.ThermalTransmittance` | W/m2K | OS:WindowMaterial:SimpleGlazingSystem U-Factor (fallback when no pane layers) | Approximated | C3 | SAM-OS-CON-001 info when fallback used |
| `ApertureParameter.LightTransmittance` | 0-1 | OS:WindowMaterial:SimpleGlazingSystem Visible Transmittance (fallback) | Approximated | C3 | - |
| `ApertureParameter.LightReflectance` | 0-1 | - | NA | C0 | Pane layers govern optics; SimpleGlazing fallback has no reflectance field. |
| `ApertureParameter.DirectSolarEnergyTransmittance` | 0-1 | - | NA | C0 | Layers govern; SimpleGlazing uses SHGC only. |
| `ApertureParameter.DirectSolarEnergyReflectance` | 0-1 | - | NA | C0 | - |
| `ApertureParameter.DirectSolarEnergyAbsorptance` | 0-1 | - | NA | C0 | - |
| `ApertureParameter.TotalSolarEnergyTransmittance` | 0-1 | OS:WindowMaterial:SimpleGlazingSystem SHGC (fallback when no pane layers) | Approximated | C3 | - |
| `ApertureParameter.PilkingtonShadingShortWavelengthCoefficient` | - | - | NA | C0 | Manufacturer shading coefficient; no EnergyPlus slot. |
| `ApertureParameter.PilkingtonShadingLongWavelengthCoefficient` | - | - | NA | C0 | - |
| `ApertureParameter.OpeningProperties` | - | - | Unsupported | C3 | SAM-OS-CON-002 info; openable fraction/discharge coefficient need AirflowNetwork or ZoneVentilation (deferred HVAC domain) |
| `ApertureParameter.Color` | - | - | NA | C0 | Display colour. |
| `ApertureParameter.FeatureShade` | - | - | Unsupported | C3 | SAM-OS-CON-002 info; no slat/shade geometry in SAM to build WindowMaterial:Blind or WindowShadingControl |

### Aperture construction parameters (`SAM.Analytical.ApertureConstructionParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `ApertureConstructionParameter.DefaultPanelType` | - | informs host typing | Derived | MVP | - |
| `ApertureConstructionParameter.Description` | - | - | NA | C0 | - |
| `ApertureConstructionParameter.Transparent` | - | SubSurface type classification (Window/Door/GlassDoor) | Derived | MVP | - |
| `ApertureConstructionParameter.Color` | - | - | NA | C0 | - |
| `ApertureConstructionParameter.DefaultFrameWidth` | m | OS:WindowProperty:FrameAndDivider Frame Width | Native | C3 | - |
| `ApertureConstructionParameter.IsInternalShadow` | - | - | Unsupported | C3 | SAM-OS-CON-002 info; TAS internal-shadow flag has no deterministic mapping |
| `ApertureConstructionParameter.LightTransmittance` | 0-1 | OS:WindowMaterial:SimpleGlazingSystem Visible Transmittance (fallback) | Approximated | C3 | - |
| `ApertureConstructionParameter.TotalSolarEnergyTransmittance` | 0-1 | OS:WindowMaterial:SimpleGlazingSystem SHGC (fallback) | Approximated | C3 | - |
| `ApertureConstructionParameter.ThermalTransmittance` | W/m2K | OS:WindowMaterial:SimpleGlazingSystem U-Factor (fallback) | Approximated | C3 | - |
| `ApertureConstructionParameter.PaneAdditionalHeatTransfer` | % | - | Unsupported | C3 | SAM-OS-CON-002 info; TAS percentage U-adjustment has no safe layer-level equivalent |
| `ApertureConstructionParameter.FrameAdditionalHeatTransfer` | % | - | Unsupported | C3 | SAM-OS-CON-002 info |

### Aperture construction structure

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `ApertureConstruction.PaneConstructionLayers` | - | OS:Construction of glazing/gas layers (FenestrationConstruction usage) | Native | MVP | - |
| `ApertureConstruction.FrameConstructionLayers` | - | OS:WindowProperty:FrameAndDivider (width, conductance, absorptances derived from layers) | Approximated | C3 | frame U = 1/sum(thickness_i/conductivity_i); width = DefaultFrameWidth else outermost frame layer thickness; solar/visible absorptance = 1 - External*Reflectance of frame material SAM-OS-CON-002 warning on invalid frame data; frameless fallback |

### Aperture structure

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Aperture.PaneFrameGeometry` | - | SubSurface polygon reduced to pane; frame width grows outward (EnergyPlus convention) | Approximated | C3 | area conservation validated: \|aperture\| ~= \|pane\| + \|frame\|; pane stays inside host; invalid inset -> frameless fallback + SAM-OS-CON-002 warning |
| `Aperture.InternalPairing` | - | SubSurface setAdjacentSubSurface | Native | MVP | - |

### Aperture types (`SAM.Analytical.ApertureType`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `ApertureType.Undefined` | - | SubSurface type via construction/material family | Derived | MVP | - |
| `ApertureType.Window` | - | SubSurface FixedWindow | Native | MVP | - |
| `ApertureType.Door` | - | SubSurface Door (opaque) / GlassDoor (transparent) | Native | MVP | - |

### Construction parameters (`SAM.Analytical.ConstructionParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `ConstructionParameter.DefaultPanelType` | - | informs panel typing | Derived | MVP | - |
| `ConstructionParameter.Description` | - | - | NA | C0 | - |
| `ConstructionParameter.Color` | - | - | NA | C0 | - |
| `ConstructionParameter.IsAir` | - | OS:Construction:AirBoundary | Native | C1 | PanelType.Air handled in MVP; C1 verifies the construction-level IsAir flag reaches the same path. |
| `ConstructionParameter.IsGround` | - | Ground outside boundary condition | Derived | MVP | Ground contact primarily driven by PanelType (SlabOnGrade/Underground*). |
| `ConstructionParameter.IsInternalShadow` | - | - | Unsupported | C3 | SAM-OS-CON-002 info |
| `ConstructionParameter.Transparent` | - | ReplaceTransparentPanels preprocessing (panel -> aperture) | Derived | MVP | - |
| `ConstructionParameter.DefaultThickness` | m | layer thickness fallback | Native | MVP | SAM-OS-CON-001 warning when fallback used |
| `ConstructionParameter.LightTransmittance` | 0-1 | - | NA | C0 | Performance descriptor; layers govern. Constructions without layers raise SAM-OS-CON-001 error. |
| `ConstructionParameter.TotalSolarEnergyTransmittance` | 0-1 | - | NA | C0 | - |
| `ConstructionParameter.ThermalTransmittance` | W/m2K | - | NA | C0 | No opaque no-mass fallback is fabricated; missing layers are an explicit error. |

### Construction structure

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Construction.LayerOrderInsideToOutside` | - | OS:Construction layers outside-first; forward/reverse variants per internal pair side | Native | MVP | Mirrored internal-pair orientation is valid and must not be changed. |

### Panel parameters (`SAM.Analytical.PanelParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `PanelParameter.Transparent` | - | ReplaceTransparentPanels preprocessing | Derived | MVP | - |
| `PanelParameter.Color` | - | - | NA | C0 | - |
| `PanelParameter.ThermalTransmittance` | W/m2K | - | NA | C0 | Performance descriptor; construction layers govern. |
| `PanelParameter.LightTransmittance` | 0-1 | - | NA | C0 | - |
| `PanelParameter.LightReflectance` | 0-1 | - | NA | C0 | - |
| `PanelParameter.DirectSolarEnergyTransmittance` | 0-1 | - | NA | C0 | - |
| `PanelParameter.DirectSolarEnergyReflectance` | 0-1 | - | NA | C0 | - |
| `PanelParameter.DirectSolarEnergyAbsorptance` | 0-1 | - | NA | C0 | - |
| `PanelParameter.TotalSolarEnergyTransmittance` | 0-1 | - | NA | C0 | - |
| `PanelParameter.PilkingtonShadingShortWavelengthCoefficient` | - | - | NA | C0 | - |
| `PanelParameter.PilkingtonShadingLongWavelengthCoefficient` | - | - | NA | C0 | - |
| `PanelParameter.Adiabatic` | - | Adiabatic outside boundary condition | Native | C1 | C1 verifies the flag is honoured by OutsideBoundaryCondition. |
| `PanelParameter.FeatureShade` | - | - | Unsupported | C3 | SAM-OS-CON-002 info |

### Panel structure

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Panel.InternalEdgeHoles` | - | - | Unsupported | C3 | SAM-OS-GEO-001 warning with panel GUID and hole geometry summary (count, per-hole and total area); holes are never fabricated into windows |

### Panel types (`SAM.Analytical.PanelType`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `PanelType.Undefined` | - | surface type from normal orientation | Derived | MVP | - |
| `PanelType.Ceiling` | - | Surface RoofCeiling, internal adjacency | Native | MVP | - |
| `PanelType.CurtainWall` | - | Wall + transparent replacement preprocessing | Native | MVP | - |
| `PanelType.Floor` | - | Surface Floor | Native | MVP | - |
| `PanelType.FloorExposed` | - | Floor, Outdoors exposure | Native | MVP | - |
| `PanelType.FloorInternal` | - | Floor, internal adjacency | Native | MVP | - |
| `PanelType.FloorRaised` | - | Floor, Outdoors exposure | Native | MVP | - |
| `PanelType.Roof` | - | Surface RoofCeiling, Outdoors + sun/wind | Native | MVP | - |
| `PanelType.Shade` | - | OS:ShadingSurface in building-level group | Native | MVP | - |
| `PanelType.SlabOnGrade` | - | Floor, Ground boundary | Native | MVP | - |
| `PanelType.SolarPanel` | - | OS:ShadingSurface (geometry only) | Approximated | C3 | SAM-OS-GEO-002 info; PV generation is not modelled |
| `PanelType.UndergroundCeiling` | - | RoofCeiling, Ground boundary | Native | MVP | - |
| `PanelType.UndergroundSlab` | - | Floor, Ground boundary | Native | MVP | - |
| `PanelType.UndergroundWall` | - | Wall, Ground boundary | Native | MVP | - |
| `PanelType.Wall` | - | Surface Wall | Native | MVP | - |
| `PanelType.WallExternal` | - | Wall, Outdoors + sun/wind | Native | MVP | - |
| `PanelType.WallInternal` | - | Wall, internal adjacency | Native | MVP | - |
| `PanelType.Air` | - | OS:Construction:AirBoundary | Native | MVP | - |

### Boundary types (`SAM.Analytical.BoundaryType`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `BoundaryType.Undefined` | - | - | NA | C0 | Resolved through PanelType mapping. |
| `BoundaryType.Ground` | - | Ground outside boundary condition | Native | MVP | - |
| `BoundaryType.Exposed` | - | Outdoors + SunExposed/WindExposed | Native | MVP | - |
| `BoundaryType.Adiabatic` | - | Adiabatic outside boundary condition | Native | MVP | - |
| `BoundaryType.Linked` | - | Surface adjacency (setAdjacentSurface) | Native | MVP | - |
| `BoundaryType.Shade` | - | OS:ShadingSurface | Native | MVP | - |

### Core material properties (`SAM.Core.Material`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Material.ThermalConductivity` | W/mK | OS:Material Conductivity / StandardGlazing Conductivity | Native | MVP | SAM-OS-MAT-001 error on non-positive values |
| `Material.Density` | kg/m3 | OS:Material Density | Native | MVP | - |
| `Material.SpecificHeatCapacity` | J/kgK | OS:Material Specific Heat | Native | MVP | - |

### Core material parameters (`SAM.Core.MaterialParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `CoreMaterialParameter.DefaultThickness` | m | layer thickness fallback | Native | MVP | SAM-OS-CON-001 warning when used |

### Analytical material parameters (`SAM.Analytical.MaterialParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `AnalyticalMaterialParameter.VapourDiffusionFactor` | - | - | Unsupported | C3 | SAM-OS-MAT-002 info; EnergyPlus moisture modelling (HAMT/EMPD) needs data SAM does not carry |

### Opaque material parameters (`SAM.Analytical.OpaqueMaterialParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `OpaqueMaterialParameter.ExternalEmissivity` | 0-1 | OS:Material Thermal Absorptance | Native | MVP | - |
| `OpaqueMaterialParameter.ExternalLightReflectance` | 0-1 | OS:Material Visible Absorptance = 1 - reflectance | Native | MVP | - |
| `OpaqueMaterialParameter.ExternalSolarReflectance` | 0-1 | OS:Material Solar Absorptance = 1 - reflectance | Native | MVP | - |
| `OpaqueMaterialParameter.IgnoreThermalTransmittanceCalculations` | - | - | NA | C0 | TAS calculation flag. |
| `OpaqueMaterialParameter.InternalEmissivity` | 0-1 | same single-sided OS:Material fields as External* | Approximated | C3 | SAM-OS-MAT-002 info when Internal differs from External; EnergyPlus opaque materials are single-sided |
| `OpaqueMaterialParameter.InternalLightReflectance` | 0-1 | single-sided OS:Material | Approximated | C3 | - |
| `OpaqueMaterialParameter.InternalSolarReflectance` | 0-1 | single-sided OS:Material | Approximated | C3 | - |

### Transparent material parameters (`SAM.Analytical.TransparentMaterialParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `TransparentMaterialParameter.ExternalEmissivity` | 0-1 | StandardGlazing Front Side IR Hemispherical Emissivity | Native | MVP | - |
| `TransparentMaterialParameter.ExternalLightReflectance` | 0-1 | StandardGlazing Front Side Visible Reflectance | Native | MVP | - |
| `TransparentMaterialParameter.ExternalSolarReflectance` | 0-1 | StandardGlazing Front Side Solar Reflectance | Native | MVP | - |
| `TransparentMaterialParameter.InternalEmissivity` | 0-1 | StandardGlazing Back Side IR Hemispherical Emissivity | Native | MVP | - |
| `TransparentMaterialParameter.InternalLightReflectance` | 0-1 | StandardGlazing Back Side Visible Reflectance | Native | MVP | - |
| `TransparentMaterialParameter.InternalSolarReflectance` | 0-1 | StandardGlazing Back Side Solar Reflectance | Native | MVP | - |
| `TransparentMaterialParameter.IsBlind` | - | - | Unsupported | C3 | SAM-OS-MAT-002 info; no slat geometry to build WindowMaterial:Blind |
| `TransparentMaterialParameter.LightTransmittance` | 0-1 | StandardGlazing Visible Transmittance at Normal Incidence | Native | MVP | - |
| `TransparentMaterialParameter.SolarTransmittance` | 0-1 | StandardGlazing Solar Transmittance at Normal Incidence | Native | MVP | - |

### Gas material parameters (`SAM.Analytical.GasMaterialParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `GasMaterialParameter.HeatTransferCoefficient` | W/m2K | OS:Material:AirGap Thermal Resistance = 1/h (opaque usage) | Native | MVP | SAM-OS-MAT-001 error on missing/non-finite/out-of-range values |
| `GasMaterialParameter.DefaultGasType` | - | OS:WindowMaterial:Gas Gas Type (fenestration usage; Air/Argon/Krypton/Xenon) | Native | MVP | SAM-OS-MAT-001 error for unsupported gas types (SulfurHexaFluoride) |

### Analytical model parameters (`SAM.Analytical.AnalyticalModelParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `AnalyticalModelParameter.NorthAngle` | rad | OS:Building North Axis [deg] | Native | C4 | convention resolved against SAM_Tas/SAM_LadybugTools usage before mapping |
| `AnalyticalModelParameter.CoolingSizingFactor` | - | OS:Sizing:Parameters Cooling Sizing Factor | Native | C4 | - |
| `AnalyticalModelParameter.HeatingSizingFactor` | - | OS:Sizing:Parameters Heating Sizing Factor | Native | C4 | - |
| `AnalyticalModelParameter.WeatherData` | - | ground temperatures + location cross-check (EPW remains the run weather) | Derived | C4 | Never silently overridden by EPW values; precedence documented. |
| `AnalyticalModelParameter.HeatingDesignDays` | - | OS:SizingPeriod:DesignDay (parametric fit from hourly SAM DesignDay) | Approximated | C4 | SAM-OS-RUN-002 warning documenting the fit; downgraded to unsupported diagnostic if the fit is unsafe |
| `AnalyticalModelParameter.CoolingDesignDays` | - | OS:SizingPeriod:DesignDay (parametric fit) | Approximated | C4 | - |
| `AnalyticalModelParameter.CaseDataCollection` | - | - | NA | C0 | Scenario/case metadata. |
| `AnalyticalModelParameter.SolarModel` | - | - | NA | C0 | TAS solar model object; EnergyPlus solar behaviour is governed by the SolarDistribution conversion option. |

### Zone parameters (`SAM.Analytical.ZoneParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `ZoneParameter.Color` | - | - | NA | C0 | - |
| `ZoneParameter.ZoneCategory` | - | - | Deferred | C0 | SAM zone grouping is an HVAC-zoning concept; Ideal Loads keeps one thermal zone per space. C5 aggregates results per SAM zone. |

### Location (`SAM.Core.Location`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Location.Latitude` | deg | OS:Site Latitude | Native | C4 | MVP takes EPW; C4 adds explicit-SAM-vs-EPW precedence with warning on mismatch. |
| `Location.Longitude` | deg | OS:Site Longitude | Native | C4 | - |
| `Location.Elevation` | m | OS:Site Elevation | Native | C4 | - |

### Location parameters (`SAM.Core.LocationParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `LocationParameter.TimeZone` | h | OS:Site Time Zone | Native | C4 | - |

### Address (`SAM.Core.Address`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Address` | - | - | NA | C0 | Postal metadata. |

### Weather data parameters (`SAM.Weather.WeatherDataParameter`)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `WeatherDataParameter.GroundTemperatures` | C | OS:Site:GroundTemperature:BuildingSurface (12 monthly values) | Native | C4 | SAM-OS-RUN-002 warning naming the fallback source (SAM -> EPW header -> EnergyPlus 18 C default) |
| `WeatherDataParameter.Country` | - | - | NA | C0 | Metadata. |
| `WeatherDataParameter.State` | - | - | NA | C0 | - |
| `WeatherDataParameter.City` | - | - | NA | C0 | - |
| `WeatherDataParameter.DataSource` | - | - | NA | C0 | - |
| `WeatherDataParameter.WMONumber` | - | - | NA | C0 | - |
| `WeatherDataParameter.TimeZone` | h | OS:Site Time Zone (with Location precedence) | Native | C4 | - |
| `WeatherDataParameter.Comments_1` | - | - | NA | C0 | - |
| `WeatherDataParameter.Comments_2` | - | - | NA | C0 | - |

### Simulation and run settings

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Simulation.EpwAssignment` | - | OS:WeatherFile via EpwFile.load | Native | MVP | - |
| `Simulation.RunPeriodExplicit` | - | OS:RunPeriod begin/end month+day | Native | C4 | - |
| `Simulation.RunPeriodRepeatCount` | - | OS:RunPeriod Number of Times Runperiod to be Repeated | Native | C4 | - |
| `Simulation.FirstDayOfWeek` | - | profile rotation + OS:YearDescription Day of Week for Start Day | Native | C4 | MVP aligns profiles; C4 also stamps YearDescription. |
| `Simulation.LeapYearCalendar` | - | OS:YearDescription Is Leap Year / Calendar Year + 8784-value schedules | Native | C4 | - |
| `Simulation.DaylightSaving` | - | OS:RunPeriodControl:DaylightSavingTime from EPW header | Approximated | C4 | Option-driven, default OFF (energy-model convention); SAM carries no DST data. |
| `Simulation.Timestep` | 1/h | OS:Timestep | Native | C4 | MVP fixed at 6/h; C4 makes it an option. |
| `Simulation.SolarDistribution` | - | OS:SimulationControl Solar Distribution | Native | C4 | - |
| `Simulation.ShadowCalculation` | - | OS:ShadowCalculation | Native | C4 | - |
| `Simulation.OutputVariableRequests` | - | OS:Output:Variable set + reporting frequency | Native | C4 | MVP fixed six hourly variables; C4 makes set and frequency options. |
| `Simulation.DdyDesignDayImport` | - | OS:SizingPeriod:DesignDay via EnergyPlusReverseTranslator | Native | C4 | Deterministic; default heating 99.6% / cooling 0.4% selection, option for all design days. |
| `Simulation.SizingRuns` | - | OS:SimulationControl sizing flags | Native | C4 | Off by default (Ideal Loads); option-enabled. Annual sums are environment-period filtered before enablement. |

### Results (SQL extraction and SAM result mapping)

| SAM property | Unit | OpenStudio / EnergyPlus representation | Status | Milestone | Conversion / policy |
|---|---|---|---|---|---|
| `Result.AnalyticalModel.ConsumptionHeating` | kWh | SUM(Zone Ideal Loads Supply Air Total Heating Energy) over the annual weather RunPeriod | Native | C5 | - |
| `Result.AnalyticalModel.ConsumptionCooling` | kWh | SUM(Zone Ideal Loads Supply Air Total Cooling Energy) over the annual weather RunPeriod | Native | C5 | - |
| `Result.AnalyticalModel.PeakHeatingLoad` | kW | max hourly heating energy / 3600 s | Native | C5 | - |
| `Result.AnalyticalModel.PeakHeatingHour` | h | hour index of peak | Native | C5 | - |
| `Result.AnalyticalModel.PeakCoolingLoad` | kW | max hourly cooling energy / 3600 s | Native | C5 | - |
| `Result.AnalyticalModel.PeakCoolingHour` | h | hour index of peak | Native | C5 | - |
| `Result.AnalyticalModel.FloorArea` | m2 | sum of space areas | Derived | C5 | - |
| `Result.AnalyticalModel.Volume` | m3 | sum of space volumes | Derived | C5 | - |
| `Result.Space.Load` | W | Zone Ideal Loads Supply Air Total Heating/Cooling Energy series | Native | C5 | - |
| `Result.Space.DesignLoad` | W | ZoneSizes CalcDesLoad (sizing runs) | Native | C5 | - |
| `Result.Space.LoadIndex` | h | peak time index | Native | C5 | - |
| `Result.Space.DryBulbTempearture` | C | Zone Mean Air Temperature (at peak / series) | Native | C5 | SAM enum member name carries a historical typo. |
| `Result.Space.ResultantTemperature` | C | Zone Operative Temperature | Approximated | C5 | Operative temperature is the closest EnergyPlus analogue of TAS resultant temperature. |
| `Result.Space.RelativeHumidity` | % | Zone Air Relative Humidity | Native | C5 | - |
| `Result.Space.HumidityRatio` | kg/kg | Zone Air Humidity Ratio | Native | C5 | - |
| `Result.Space.UnmetHours` | h | Zone Heating/Cooling Setpoint Not Met Time | Native | C5 | - |
| `Result.Space.OccupiedUnmetHours` | h | Zone Setpoint Not Met While Occupied Time | Native | C5 | - |
| `Result.Space.SolarGain` | W | Zone Windows Total Transmitted Solar Radiation Rate | Native | C5 | - |
| `Result.Space.LightingGain` | W | Zone Lights Total Heating Rate | Native | C5 | - |
| `Result.Space.EquipmentSensibleGain` | W | Zone Electric Equipment Radiant+Convective Heating Rate | Native | C5 | - |
| `Result.Space.EquipmentLatentGain` | W | Zone Electric Equipment Latent Gain Rate | Native | C5 | - |
| `Result.Space.OccupancySensibleGain` | W | Zone People Sensible Heating Rate | Native | C5 | - |
| `Result.Space.OccupancyLatentGain` | W | Zone People Latent Gain Rate | Native | C5 | - |
| `Result.Space.InfiltrationGain` | W | Zone Infiltration Sensible Heat Gain/Loss Energy | Native | C5 | - |
| `Result.Space.GlazingExternalConduction` | W | Zone Windows Total Heat Gain/Loss Rate | Approximated | C5 | - |
| `Result.Space.OpaqueExternalConduction` | W | Surface Average Face Conduction Heat Transfer Rate (external opaque aggregate) | Approximated | C5 | - |
| `Result.Space.OccupiedHours` | h | occupancy schedule x zone temperature series | Derived | C5 | - |
| `Result.Space.MaxMinDryBulbTemperature` | C | series max/min + indices | Derived | C5 | - |
| `Result.Space.AirMovementGain` | W | - | NA | C0 | Inter-zone air movement needs AirflowNetwork; not populated. |
| `Result.Space.BuildingHeatTransfer` | W | - | NA | C0 | TAS aggregate without a direct EnergyPlus variable. |
| `Result.Space.ApertureFlow` | kg/s | - | NA | C0 | Aperture airflow needs AirflowNetwork. |
| `Result.Space.Pollutant` | ppm | - | NA | C0 | Contaminant modelling out of scope. |
| `Result.Surface.Temperatures` | C | Surface Inside/Outside Face Temperature | Native | C5 | - |
| `Result.Surface.Conduction` | W | Surface Inside/Outside Face Conduction Heat Transfer Rate | Native | C5 | - |
| `Result.Surface.Convection` | W | Surface Inside/Outside Face Convection Heat Gain Rate | Native | C5 | - |
| `Result.Surface.LongWave` | W | Surface Inside/Outside Face Net Thermal Radiation Heat Gain Rate | Native | C5 | - |
| `Result.Surface.SolarGain` | W | Surface Inside/Outside Face absorbed/incident solar | Approximated | C5 | - |
| `Result.Surface.Condensation` | g/m2 | - | NA | C0 | No EnergyPlus condensation-mass model in scope. |
| `Result.Surface.ApertureOpening` | % | - | NA | C0 | Opening modulation needs AirflowNetwork. |
| `Result.Zone.Aggregates` | - | member-space aggregation per SAM zone group | Derived | C5 | - |
| `Result.AdjacencyCluster.UnmetHours` | h | building aggregate of zone unmet hours | Native | C5 | - |
| `Result.Identity.SamGuidMapping` | - | OpenStudioObjectMap source GUID <-> OpenStudio handle/name | Native | MVP | - |
| `Result.SimulationRuntime` | s | runner wall-clock + eplusout.err warning/severe/fatal counts | Native | C5 | - |


## Enum completeness enforcement

The manifest's `coveredEnums` list names every SAM parameter/type enum whose **entire live
membership** must appear in the manifest. The C7 completeness test reflects over those enums in
the referenced SAM assemblies and fails when:

- a live enum member has no manifest entry (a *new* SAM property would otherwise be silently
  omitted);
- a manifest entry references an enum member that no longer exists (stale coverage);
- an `Unsupported` entry declares no diagnostic policy.

Concept rows (ids not backed by an enum member, e.g. `Profile.LeapYear8784`,
`Aperture.PaneFrameGeometry`, `Simulation.*`, `Result.*`) are maintained by review; they exist so
that behavioural semantics — not just parameter slots — are classified.

## Counts at C0 (before implementation)

Generated from the manifest: 277 entries — Native 135, Derived 28, Approximated 21,
Unsupported 20, Deferred 17, NA 56. Milestones: MVP 90, C1 3, C2 37, C3 23, C4 26, C5 36,
classification-only (C0) 62.

Stage L review correction (P2-01): the C0 audit transcribed two commented-out members of
`SAM.Analytical.MaterialParameter` (`TypeName`, `Description`); those stale rows were removed
from the manifest and this document, and the stale-id reverse check in `CompletenessTests`
now fails on any manifest id that stops naming a live enum member.

These counts change as milestones land; the final counts are reported in
`docs/openstudio-analytical-completeness-status.md`.

## Semantic references

- `SAM` core (sibling repository) is authoritative for units and semantics; enum member lists
  were extracted mechanically from its source at review time.
- `SAM_LadybugTools` (HoneybeeSchema 2.6, PR #8) served as a semantic cross-reference for frames,
  latent gains and humidistat handling. No Honeybee runtime dependency is introduced.
- EnergyPlus Input/Output Reference (V24.1/OpenStudio 3.10.0) for object field semantics.
