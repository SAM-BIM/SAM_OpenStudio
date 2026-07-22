# OpenStudio → SAM Analytical Import Coverage

Status: reviewed 2026-07-21 (branch `feature/openstudio-to-sam-analytical`).

This document is the complete audit of what the reverse (OpenStudio → SAM) import covers. It is
**generated from and kept in lockstep with** the machine-readable manifest
[`tests/resources/openstudio-to-sam-coverage.json`](../tests/resources/openstudio-to-sam-coverage.json);
the `ReverseCoverageTests` enforce the manifest (never this Markdown). When coverage changes,
update the manifest first and regenerate the tables here.

The forward direction has its own document,
[`SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md`](SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md). The two are
independent: a concept can be Native one way and Unsupported the other.

## Classification scheme

| Status | Meaning |
|---|---|
| **Native** | Direct SAM representation; values pass through. |
| **Derived** | Deterministically calculated from OpenStudio data (formula documented per row). |
| **Approximated** | Mapped under a documented assumption; a structured diagnostic names the approximation. |
| **Unsupported** | No safe SAM equivalent; a structured diagnostic is emitted when the source object is present. Nothing is silently dropped. |
| **Deferred** | Belongs to the detailed-HVAC programme. Reported through `SAM-OSI-HVAC-001`, never translated. |
| **NotApplicable** | No import semantics: run configuration, reporting, or a concept SAM re-derives on export. |

## Totals

| Status | Entries |
|---|---|
| Native | 56 |
| Derived | 11 |
| Approximated | 16 |
| Unsupported | 45 |
| Deferred | 9 |
| NotApplicable | 5 |
| **Total** | **142** |

## Explicitly deferred domains

AirLoopHVAC and PlantLoop translation into SAM Systems; zone equipment and control sequences;
service water heating and refrigeration; AirflowNetwork; daylighting controls;
EnergyManagementSystem; output requests and reporting configuration.

## Coverage tables

### Model, site and weather

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `Model.Building.Name` | `AnalyticalModel.Name` | **Native** | - | The stamped SAM.Name feature takes precedence over the sanitised OpenStudio object name. |
| `Model.Building.NorthAxis` | `AnalyticalModelParameter.NorthAngle` | **Native** | - | radians = degrees * pi / 180 (exact inverse of the forward conversion) |
| `Model.Site` | `Core.Location` | **Native** | `SAM-OSI-WEA-001` | No Location is created when latitude, longitude and elevation are all exactly zero - the OpenStudio default means the site was never set. |
| `Model.Site.Terrain` | - | **Unsupported** | `SAM-OSI-WEA-001` | SAM Location carries no terrain class. |
| `Model.Site.TimeZone` | - | **Unsupported** | `SAM-OSI-WEA-001` | SAM Location carries no time zone. |
| `Model.WeatherFile` | `AnalyticalModelParameter.WeatherData + OpenStudioSourceParameter.WeatherFilePath` | **Derived** | `SAM-OSI-WEA-001` | When the referenced EPW is found on disk its hourly weather is loaded into SAM WeatherData and embedded (the slot the forward exporter reads back). When the file is absent, or ImportWeatherData is off, only the path is recorded - a reference is never presented as embedded weather. |
| `Model.SizingPeriod.DesignDay` | `AnalyticalModelParameter.Heating/CoolingDesignDays` | **Approximated** | `SAM-OSI-WEA-001` | Imported by name, date and heating/cooling role only: an EnergyPlus design day is parametric while a SAM DesignDay is an hourly weather day. |
| `Model.RunPeriod` | - | **Unsupported** | `SAM-OSI-SET-001` | SAM AnalyticalModel carries no run period; it is set by the forward conversion options. |
| `Model.Timestep` | - | **Unsupported** | `SAM-OSI-SET-001` |  |
| `Model.ShadowCalculation` | - | **Unsupported** | `SAM-OSI-SET-001` |  |
| `Model.YearDescription` | - | **NotApplicable** | - | Calendar configuration of the run, not of the building. |

### Spaces and zones

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:Space` | `Space` | **Native** | - | Always 1:1; never collapsed by ThermalZone. |
| `OS:Space.Transformation` | - | **Native** | - | buildingTransformation applied to every surface vertex; SAM-authored OSMs have identity here. |
| `OS:Space.FloorArea` | `SpaceParameter.Area` | **Derived** | - | shell area of the imported bounding faces; OpenStudio floorArea() only when no closed shell exists |
| `OS:Space.Volume` | `SpaceParameter.Volume` | **Derived** | - | shell volume of the imported bounding faces; OpenStudio volume() only when no closed shell exists |
| `OS:Space.Location` | `Space.Location` | **Derived** | `SAM-OSI-ZONE-002` | internal point of the closed shell |
| `OS:BuildingStory` | `SpaceParameter.LevelName` | **Native** | - |  |
| `OS:ThermalZone` | `OpenStudioSourceParameter.ThermalZoneName` | **Native** | - | Recorded per space; controls are applied from it. |
| `OS:ThermalZone.MultiSpace` | - | **Native** | `SAM-OSI-ZONE-001` | Each space stays a separate SAM Space and they share the zone's controls. |
| `OS:Space.Multiplier` | - | **Unsupported** | `SAM-OSI-ZONE-002` | SAM has no space multiplier. |
| `OS:SpaceInfiltration.DesignFlowRate.ACH` | `InternalConditionParameter.InfiltrationAirChangesPerHour` | **Native** | - |  |
| `OS:SpaceInfiltration.DesignFlowRate.Other` | - | **Unsupported** | `SAM-OSI-LOAD-001` | Flow-based methods need per-space geometry a shared space type does not have. |
| `OS:SpaceInfiltration.EffectiveLeakageArea` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:SpaceInfiltration.FlowCoefficient` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:SpaceType` | `InternalCondition` | **Native** | - | One deduplicated internal condition per space type. |
| `OS:Space.SpaceLevelLoads` | - | **Unsupported** | `SAM-OSI-LOAD-002` | SAM has no per-space load layer on top of an internal condition. |

### Surfaces

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:Surface.Wall.Outdoors` | `PanelType.WallExternal` | **Native** | - |  |
| `OS:Surface.Wall.Surface` | `PanelType.WallInternal` | **Native** | - |  |
| `OS:Surface.Wall.Adiabatic` | `PanelType.WallInternal + PanelParameter.Adiabatic` | **Native** | - |  |
| `OS:Surface.Wall.Ground` | `PanelType.UndergroundWall` | **Native** | - |  |
| `OS:Surface.Floor.Outdoors` | `PanelType.FloorExposed` | **Native** | - |  |
| `OS:Surface.Floor.Surface` | `PanelType.FloorInternal` | **Native** | - |  |
| `OS:Surface.Floor.Adiabatic` | `PanelType.FloorInternal + PanelParameter.Adiabatic` | **Native** | - |  |
| `OS:Surface.Floor.Ground` | `PanelType.SlabOnGrade` | **Native** | - |  |
| `OS:Surface.RoofCeiling.Outdoors` | `PanelType.Roof` | **Native** | - |  |
| `OS:Surface.RoofCeiling.Surface` | `PanelType.Ceiling` | **Native** | - |  |
| `OS:Surface.RoofCeiling.Adiabatic` | `PanelType.Ceiling + PanelParameter.Adiabatic` | **Native** | - |  |
| `OS:Surface.RoofCeiling.Ground` | `PanelType.UndergroundCeiling` | **Native** | - |  |
| `OS:Surface.Foundation` | `ground-contact PanelType` | **Native** | - | The whole Ground/Foundation/preprocessor family is treated as ground contact. |
| `OS:Surface.OtherSideCoefficients` | - | **Unsupported** | `SAM-OSI-BC-001` | Falls back to adiabatic; SAM cannot carry an externally specified surface temperature. |
| `OS:Surface.OtherSideConditionsModel` | - | **Unsupported** | `SAM-OSI-BC-001` | Falls back to adiabatic. |
| `OS:Surface.AdjacentSurface` | - | **Native** | - | Authoritative: the two surfaces of an interzone partition become ONE SAM Panel related to two Spaces. |
| `OS:Surface.AdjacencyByGeometry` | - | **Approximated** | `SAM-OSI-ADJ-001` | Only for a Surface boundary with no adjacency handle; requires coincident internal points, areas within 1 percent and opposed normals. |
| `OS:Surface.AdjacencyUnresolved` | - | **Approximated** | `SAM-OSI-ADJ-002` | Demoted to adiabatic, matching the forward direction's single-sided internal panel. |
| `OS:Surface.Geometry` | - | **Native** | `SAM-OSI-GEO-001` | Validated for vertex count, planarity, duplicates, self-intersection and minimum area; never repaired. |
| `OS:Surface.Geometry.VertexCleaning` | - | **Native** | `SAM-OSI-GEO-002` | Duplicate and collinear vertices are removed while normalising a boundary. Shape, area and normal are unchanged, so this is reported at Information severity and kept distinct from invalid geometry. |
| `OS:Surface.SunWindExposure` | - | **NotApplicable** | - | Derived by EnergyPlus from the boundary condition; SAM re-derives it on export. |

### SubSurfaces and fenestration detail

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:SubSurface.FixedWindow` | `ApertureType.Window` | **Native** | - |  |
| `OS:SubSurface.OperableWindow` | `ApertureType.Window` | **Approximated** | `SAM-OSI-APX-001` | SAM has no openability flag. |
| `OS:SubSurface.Skylight` | `ApertureType.Window` | **Native** | - |  |
| `OS:SubSurface.Door` | `ApertureType.Door` | **Native** | - |  |
| `OS:SubSurface.GlassDoor` | `ApertureType.Door` | **Native** | - |  |
| `OS:SubSurface.OverheadDoor` | `ApertureType.Door` | **Approximated** | `SAM-OSI-APX-001` |  |
| `OS:SubSurface.TubularDaylightDome` | `ApertureType.Window` | **Approximated** | `SAM-OSI-APX-001` | Imported as the glazed opening; SAM has no light pipe. |
| `OS:SubSurface.TubularDaylightDiffuser` | `ApertureType.Window` | **Approximated** | `SAM-OSI-APX-001` |  |
| `OS:SubSurface.Unknown` | - | **Unsupported** | `SAM-OSI-SUB-001` |  |
| `OS:SubSurface.Multiplier` | - | **Approximated** | `SAM-OSI-APX-001` | SAM apertures have no multiplier; the geometry is imported once. |
| `OS:SubSurface.Containment` | - | **Native** | `SAM-OSI-GEO-001` | Validated by Panel.AddAperture; a subsurface outside its host is reported, not attached. |
| `OS:WindowPropertyFrameAndDivider` | - | **Unsupported** | `SAM-OSI-CON-001` | Frame and divider live outside the construction; SAM frame layers are never fabricated from them. |
| `OS:ShadingControl` | - | **Unsupported** | `SAM-OSI-CON-001` |  |
| `OS:DaylightingDeviceShelf` | - | **Unsupported** | `SAM-OSI-SUB-001` |  |

### Shading

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:ShadingSurface.Site` | `PanelType.Shade` | **Native** | - |  |
| `OS:ShadingSurface.Building` | `PanelType.Shade` | **Native** | - |  |
| `OS:ShadingSurface.Space` | `PanelType.Shade` | **Native** | - | Group transformation composed with the owning space's building transformation. |
| `OS:ShadingSurfaceGroup.Identity` | `OpenStudioSourceParameter.ShadingGroupType/Name` | **Native** | - |  |
| `OS:ShadingSurfaceGroup.ShadedSurface` | - | **Approximated** | `SAM-OSI-APX-001` | SAM shade panels are free-standing; the association is metadata only. |

### Materials

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:Material.StandardOpaqueMaterial` | `Core.OpaqueMaterial` | **Native** | - |  |
| `OS:Material.MasslessOpaqueMaterial` | `Core.OpaqueMaterial` | **Approximated** | `SAM-OSI-APX-001` | conductivity = 0.025 m / R; resistance preserved exactly, near-zero thermal mass assigned |
| `OS:Material.AirGap` | `Core.GasMaterial` | **Derived** | - | HeatTransferCoefficient = 1 / thermalResistance (exact inverse of the forward R = 1/h) |
| `OS:WindowMaterial.Glazing` | `Core.TransparentMaterial` | **Native** | - | EnergyPlus Front/Back map to SAM External/Internal, matching the forward direction. |
| `OS:WindowMaterial.SimpleGlazingSystem` | `Core.TransparentMaterial` | **Approximated** | `SAM-OSI-APX-001` | conductivity = thickness * U; solar transmittance taken from SHGC (an overstatement, since SHGC includes inward-flowing absorbed radiation) |
| `OS:WindowMaterial.Gas` | `Core.GasMaterial` | **Native** | - | HeatTransferCoefficient = getThermalConductance(293.15 K) |
| `OS:WindowMaterial.GasMixture` | `Core.GasMaterial` | **Approximated** | `SAM-OSI-MAT-001` | Conductance preserved; composition lost - SAM carries a single gas type. |
| `OS:WindowMaterial.Blind` | - | **Unsupported** | `SAM-OSI-MAT-001` | Name-only placeholder; no properties substituted. |
| `OS:WindowMaterial.Shade` | - | **Unsupported** | `SAM-OSI-MAT-001` |  |
| `OS:WindowMaterial.Screen` | - | **Unsupported** | `SAM-OSI-MAT-001` |  |
| `OS:WindowMaterial.GlazingRefractionExtinctionMethod` | - | **Unsupported** | `SAM-OSI-MAT-001` |  |
| `OS:MaterialProperty.PhaseChange` | - | **Unsupported** | `SAM-OSI-MAT-001` | SAM materials have no phase-change model. |

### Constructions

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:Construction.Opaque` | `Construction` | **Native** | - | Layer order preserved outside-first. |
| `OS:Construction.Fenestration` | `ApertureConstruction` | **Native** | - | Pane layers only; frame layers are never fabricated. |
| `OS:Construction.AirBoundary` | `PanelType.Air` | **Native** | - | A layer-free SAM construction; the air behaviour is carried by the panel type. |
| `OS:Construction.InternalSource` | - | **Unsupported** | `SAM-OSI-CON-001` |  |
| `OS:Construction.CfactorUndergroundWall` | - | **Unsupported** | `SAM-OSI-CON-001` |  |
| `OS:Construction.FfactorGroundFloor` | - | **Unsupported** | `SAM-OSI-CON-001` |  |
| `OS:Construction.WindowDataFile` | - | **Unsupported** | `SAM-OSI-CON-001` |  |
| `OS:Construction.Missing` | - | **Unsupported** | `SAM-OSI-CON-001` | A surface with no construction gets a named, layer-free placeholder - never a default build-up. |
| `OS:Construction.UnreferencedPruning` | - | **Native** | - | Materials and profiles the imported topology does not reference are removed. |

### Loads and controls

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:People` | `InternalConditionParameter.AreaPerPerson + Occupancy*GainPerPerson` | **Derived** | `SAM-OSI-LOAD-001` | areaPerPerson = 1 / peopleperSpaceFloorArea; sensible = activityLevel * sensibleHeatFraction; latent = activityLevel * (1 - sensibleHeatFraction) |
| `OS:People.AutocalculatedSensibleFraction` | - | **Approximated** | `SAM-OSI-APX-001` | No fixed split exists; the whole metabolic rate is imported as sensible. |
| `OS:People.NumberOfPeopleMethod` | - | **Unsupported** | `SAM-OSI-LOAD-001` | An absolute count cannot become a density on a shared space type without inventing an area. |
| `OS:Lights` | `InternalConditionParameter.LightingGainPerArea + LightingProfileName` | **Native** | - |  |
| `OS:Lights.RadiantVisibleFractions` | `InternalConditionParameter.LightingRadiantProportion / LightingViewCoefficient` | **Native** | - |  |
| `OS:ElectricEquipment` | `InternalConditionParameter.EquipmentSensibleGainPerArea / EquipmentLatentGainPerArea` | **Native** | - |  |
| `OS:ElectricEquipment.PartLatent` | - | **Approximated** | `SAM-OSI-APX-001` | A single part-latent instance is apportioned; the two halves then share one schedule. |
| `OS:GasEquipment` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:SteamEquipment` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:HotWaterEquipment` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:OtherEquipment` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:Luminaire` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:ElectricEquipment.ITE.AirCooled` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:InternalMass` | - | **Unsupported** | `SAM-OSI-LOAD-001` |  |
| `OS:DesignSpecification.OutdoorAir` | `InternalConditionParameter.SupplyAirFlow*` | **Derived** | - | each populated outdoor-air field maps to its SAM counterpart: perPerson -> SupplyAirFlowPerPerson, perFloorArea -> SupplyAirFlowPerArea, airChangesPerHour -> SupplyAirChangesPerHour, flowRate -> SupplyAirFlow |
| `OS:ThermostatSetpoint.DualSetpoint` | `InternalConditionParameter.Heating/CoolingProfileName` | **Native** | - | SAM stores setpoints as profiles whose values are the temperatures, so setback schedules survive. |
| `OS:ZoneControl.Humidistat` | `InternalConditionParameter.Humidification/DehumidificationProfileName` | **Native** | - |  |

### Schedules

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:Schedule.Constant` | `Profile` | **Native** | - | 8760 identical hourly values |
| `OS:Schedule.Ruleset` | `Profile` | **Derived** | - | getDaySchedules over the whole year, sampled at the end of each hour; weekday/weekend rules, seasonal ranges and rule priority resolved by OpenStudio |
| `OS:Schedule.Day` | - | **Derived** | - | Read through the ruleset expansion. |
| `OS:Schedule.Rule` | - | **Derived** | - | Applied by the ruleset expansion; never flattened away. |
| `OS:Schedule.FixedInterval.Hourly` | `Profile` | **Native** | - |  |
| `OS:Schedule.FixedInterval.SubHourly` | - | **Approximated** | `SAM-OSI-SCH-001` | Averaged into hourly means so the daily total is preserved. |
| `OS:Schedule.Compact` | - | **Unsupported** | `SAM-OSI-SCH-001` | The load is imported without a profile; no constant is substituted. |
| `OS:Schedule.File` | - | **Unsupported** | `SAM-OSI-SCH-001` |  |
| `OS:Schedule.VariableInterval` | - | **Unsupported** | `SAM-OSI-SCH-001` |  |
| `OS:ScheduleTypeLimits` | - | **NotApplicable** | - | SAM ProfileType carries the semantic instead. |

### HVAC and deferred domains

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `OS:ZoneHVAC.IdealLoadsAirSystem` | - | **Derived** | - | Recognised as the conditioned state; SAM's own export targets Ideal Loads. |
| `OS:AirLoopHVAC` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:PlantLoop` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:ZoneHVAC.Equipment` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:AvailabilityManager` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:WaterUseEquipment` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:Refrigeration` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:AirflowNetwork` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:Daylighting.Controls` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:EnergyManagementSystem` | - | **Deferred** | `SAM-OSI-HVAC-001` |  |
| `OS:Output.Variable` | - | **NotApplicable** | - | Reporting configuration, not building data. |

### Identity

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `Identity.SAM.Guid` | - | **Native** | `SAM-OSI-ID-001` | Full SAM Guid restored from OS:AdditionalProperties. |
| `Identity.SAM.Type` | - | **Native** | `SAM-OSI-ID-001` | Rejects identity metadata belonging to a different SAM object kind. |
| `Identity.SAM.Name` | - | **Native** | - | The unsanitised SAM name, unavailable from the sanitised object name. |
| `Identity.NameGuidSuffix` | - | **NotApplicable** | - | 8 hex characters of a 128-bit Guid: a matching and diagnostic key only, never a restorable identity. |
| `Identity.ThirdPartyModel` | - | **Native** | - | No metadata is the normal third-party case and is never reported as a problem. |
| `Identity.OpenStudioHandle` | `OpenStudioSourceParameter.SourceHandle` | **Native** | - |  |

### Input and workflow

| OpenStudio concept | SAM target | Status | Diagnostic | Conversion / policy |
|---|---|---|---|---|
| `Input.PathInvalid` | - | **Unsupported** | `SAM-OSI-IN-001` |  |
| `Input.ExtensionUnsupported` | - | **Unsupported** | `SAM-OSI-IN-002` |  |
| `Input.OsmLoadFailure` | - | **Unsupported** | `SAM-OSI-OSM-001` | Never returns an empty AnalyticalModel. |
| `Input.OsmVersionTranslation` | - | **Native** | `SAM-OSI-OSM-002` | Older OSMs are upgraded on load; translator messages are surfaced. |
| `Osw.Parse` | - | **Native** | `SAM-OSI-OSW-001` |  |
| `Osw.SeedMissing` | - | **Unsupported** | `SAM-OSI-OSW-002` | Blocking when execution is disabled. |
| `Osw.SeedNotFound` | - | **Unsupported** | `SAM-OSI-OSW-003` |  |
| `Osw.SeedOnlyImport` | - | **Approximated** | `SAM-OSI-OSW-004` | The seed is imported and the unapplied measures are stated. |
| `Osw.ExecutionFailure` | - | **Unsupported** | `SAM-OSI-OSW-005` |  |
| `Osw.FinalOsmNotFound` | - | **Unsupported** | `SAM-OSI-OSW-006` |  |
| `Osw.EnergyPlusMeasure` | - | **Unsupported** | `SAM-OSI-OSW-007` | IDF-only changes cannot be represented in the OSM. |
