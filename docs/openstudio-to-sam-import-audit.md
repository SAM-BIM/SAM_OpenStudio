# OpenStudio → SAM Analytical Import — B0 audit

Status: audited 2026-07-21 on branch `feature/openstudio-to-sam-analytical`, based on
`sow/2026-Q3` (`3fd556a`).

This document records the pre-implementation audit required before the reverse
(OpenStudio → SAM) conversion is written: the verified baseline, the existing infrastructure
that must be reused rather than duplicated, the confirmed OpenStudio 3.10.0 .NET API surface,
and the reverse-mapping matrix that governs the implementation.

The companion machine-readable manifest is
[`tests/resources/openstudio-to-sam-coverage.json`](../tests/resources/openstudio-to-sam-coverage.json);
the reverse coverage tables live in
[`SAM_OPENSTUDIO_TO_SAM_COVERAGE.md`](SAM_OPENSTUDIO_TO_SAM_COVERAGE.md) and are generated from it.

---

## 1. Baseline

Measured on this machine before any change (Visual Studio 2026 / MSBuild 18, x64):

| Item | Command | Result |
|---|---|---|
| Build | `MSBuild SAM_OpenStudio.sln -p:Configuration=Debug -p:Platform=x64` | **Succeeded**, exit 0, 0 errors |
| Tests | `dotnet test tests/SAM.Analytical.OpenStudio.Tests --no-build` | **237 total — 235 passed, 2 skipped, 0 failed** (2 m 16 s) |

The two skipped tests (`RealSqlFixture_SpaceAndSurfaceResults_NoThrow`,
`CreateDesignDays_RealSqlReproductionFixture_NoThrow`) are conditional on an
uncommitted real EnergyPlus SQLite fixture and are skipped on a clean checkout.

---

## 2. Existing infrastructure to reuse (never duplicate)

| Concern | Existing implementation | Reverse-import use |
|---|---|---|
| OSM load + version translation | `SAM.Core.OpenStudio.Create.Model(string)` — `VersionTranslator.loadModel` | Reused as-is; hardened with an out-parameter overload that reports the translation outcome instead of collapsing every failure to `null`. |
| CLI discovery | `SAM.Core.OpenStudio.Query.OpenStudioCliPath(string)` | Reused unchanged. |
| Async run, cancellation, process-tree kill, unique run dirs, lock file | `OpenStudioSimulationRunner.Run(string path, …)` / `ExecuteCli` / `ProcessJobObject` | Reused unchanged for OSW execution (case 2/3). |
| Diagnostics | `Core.OpenStudio.OpenStudioDiagnostic`, `…Severity`, `…DiagnosticCodes` | Same immutable record and severity model; **new** `OpenStudioImportDiagnosticCodes` holds the reverse-only codes so forward codes stay stable. |
| Statistics | `Core.OpenStudio.OpenStudioConversionStatistics` | Reused by the import context. |
| Object traceability | `OpenStudioObjectMap` / `OpenStudioObjectReference` | Reused (SAM Guid ↔ OpenStudio name). |
| Geometry validation | `SAM.Geometry.OpenStudio.Query.ValidatePolygon` / `CleanVertices` / `IsClockwise` / `Normal` / `IsPlanar` / `Area` / `EnergyPlusSideCount` | Reused; a new `Convert/ToSAM` direction adds `Point3d`/`Point3dVector` → SAM. |
| Naming | `Core.OpenStudio.Query.OpenStudioName`, `Analytical.OpenStudio.Query.TryGetGuidSuffix` | Reused for matching/diagnostics only — see §6. |

---

## 3. Confirmed OpenStudio 3.10.0 .NET API surface

Verified by reflecting the referenced `OpenStudio.dll` (4 099 public types) — nothing below is assumed.

* `ModelObject.additionalProperties() : AdditionalProperties`, `hasAdditionalProperties()`,
  `removeAdditionalProperties()`. `AdditionalProperties.setFeature(string, string|double|int|bool)`,
  `getFeatureAsString/Double/Integer/Boolean`, `hasFeature`, `featureNames`.
  → the round-trip identity channel (§6) exists natively; no OSM schema extension is needed.
* `PlanarSurface` (base of `Surface`, `SubSurface`, `ShadingSurface`): `vertices() : Point3dVector`,
  `outwardNormal()`, `plane()`, `grossArea()`, `netArea()`, `construction() : OptionalConstructionBase`,
  `space() : OptionalSpace`, `isAirWall()`, `azimuth()`, `tilt()`.
* `PlanarSurfaceGroup` (base of `Space`, `ShadingSurfaceGroup`): `transformation()`,
  `buildingTransformation()`, `siteTransformation()`, `directionofRelativeNorth()`,
  `xOrigin()/yOrigin()/zOrigin()`. **Surface vertices are group-local, not world** — the group
  transformation must be applied on import.
* `Transformation.Multiply(Point3dVector) : Point3dVector` — the vertex transform used.
* `Surface`: `surfaceType()`, `outsideBoundaryCondition()`, `adjacentSurface() : OptionalSurface`,
  `subSurfaces() : SubSurfaceVector`, `isGroundSurface()`, `sunExposure()`, `windExposure()`.
* `SubSurface`: `subSurfaceType()`, `surface() : OptionalSurface`, `adjacentSubSurface()`,
  `multiplier()`, `roughOpeningVertices()`, `assemblyUFactor()`, `assemblySHGC()`,
  `daylightingDeviceTubular()`.
* `ShadingSurfaceGroup`: `shadingSurfaceType()` (`Site`|`Building`|`Space`), `space()`,
  `shadedSurface()`, `shadedSubSurface()`, `shadingSurfaces()`.
* `Space`: `thermalZone()`, `spaceType()`, `buildingStory()`, `surfaces()`, `shadingSurfaceGroups()`,
  `volume()`, `floorArea()`, `ceilingHeight()`, `multiplier()`, `people()`, `lights()`,
  `electricEquipment()`, `gasEquipment()`, `spaceInfiltrationDesignFlowRates()`,
  `designSpecificationOutdoorAir()`, `isVolumeAutocalculated()`.

Boundary-condition string domain (`Surface.validOutsideBoundaryConditionValues()`):
`Adiabatic`, `Surface`, `Outdoors`, `Foundation`, `Ground`, `GroundFCfactorMethod`,
`OtherSideCoefficients`, `OtherSideConditionsModel`, `GroundSlabPreprocessorAverage` (+ variants),
`GroundBasementPreprocessorAverageWall` (+ variants).

SubSurface type domain: `FixedWindow`, `OperableWindow`, `Door`, `GlassDoor`,
`OverheadDoor`, `Skylight`, `TubularDaylightDome`, `TubularDaylightDiffuser`.

---

## 4. Reverse-mapping matrix

Status vocabulary matches the forward manifest: **Native**, **Derived**, **Approximated**,
**Unsupported**, **Deferred**, **NA**.

### 4.1 Model, site and metadata

| OpenStudio source | SAM target | Status | Diagnostic | Tests |
|---|---|---|---|---|
| `Building.name` / `Model` name | `AnalyticalModel.Name` | Native | – | OSM-M1 |
| `Building.northAxis` | `AnalyticalModelParameter`-carried north axis (metadata) | Native | – | OSM-M1 |
| `Site.name/latitude/longitude/elevation` | `Core.Location` | Native | – | OSM-12 |
| `Site.timeZone` | model metadata | Native | – | OSM-12 |
| `Site.terrain`, `Site` ground temperatures | model metadata / not represented | Approximated | `SAM-OSI-WEA-001` | OSM-12 |
| `BuildingStory` | `SpaceParameter.LevelName` per space | Native | – | OSM-M1 |
| `DesignDay` (`SizingPeriod:DesignDay`) | `AnalyticalModelParameter.Heating/CoolingDesignDays` | Approximated | `SAM-OSI-WEA-001` | OSM-12 |
| `WeatherFile` (`OS:WeatherFile`) | `AnalyticalModelParameter.WeatherData` when the EPW is on disk, else path metadata | Derived | `SAM-OSI-WEA-001` | OSM-12 |
| `SimulationControl`, `Timestep`, `RunPeriod`, `ShadowCalculation` | model metadata where a SAM equivalent exists | Approximated | `SAM-OSI-SET-001` | OSM-12 |

An OSM import **never requires an EPW** to convert geometry and analytical data. Embedded SAM
`WeatherData` is created only when actual hourly weather is available: when the referenced EPW is
found on disk it is loaded and embedded, and when it is not, only the path is recorded — a
`WeatherFile` reference alone is never presented as embedded weather.

### 4.2 Spaces and zones

| OpenStudio source | SAM target | Status | Diagnostic | Tests |
|---|---|---|---|---|
| `Space` | `Space` (1:1, never collapsed by zone) | Native | – | OSM-1, OSM-2 |
| `Space.name` | `Space.Name` | Native | – | OSM-1 |
| `Space.buildingStory` | `SpaceParameter.LevelName` | Native | – | OSM-M1 |
| space shell → internal point | `Space.Location` | Derived | `SAM-OSI-APX-001` when the shell will not close and the vertex centroid is used | OSM-1 |
| `Space.floorArea()` / shell area | `SpaceParameter.Area` | Derived | – | OSM-1 |
| `Space.volume()` / shell volume | `SpaceParameter.Volume` | Derived | – | OSM-1 |
| `ThermalZone` with N > 1 spaces | N SAM spaces sharing zone-derived controls | Native | `SAM-OSI-ZONE-001` (information) | OSM-M2 |
| `ThermalZone.thermostatSetpointDualSetpoint` | `InternalCondition` heating/cooling setpoints | Native | `SAM-OSI-LOAD-001` | OSM-11 |
| `ZoneHVACIdealLoadsAirSystem` | `SpaceParameter`-level conditioned flag | Derived | – | OSM-11 |
| `AirLoopHVAC`, `PlantLoop`, zone equipment | *not imported* | Deferred | `SAM-OSI-HVAC-001` | OSM-13 |
| `Space` with no surfaces | reported, still created | – | `SAM-OSI-ZONE-002` | OSM-13 |

### 4.3 Surfaces → panels

`PanelType` is derived from **both** `surfaceType()` and `outsideBoundaryCondition()`:

| `surfaceType` | `outsideBoundaryCondition` | SAM `PanelType` | Status |
|---|---|---|---|
| Wall | Outdoors | `WallExternal` | Native |
| Wall | Surface | `WallInternal` | Native |
| Wall | Adiabatic | `WallInternal` + `PanelParameter.Adiabatic` | Native |
| Wall | Ground / Foundation / Ground\* | `UndergroundWall` | Native |
| Floor | Outdoors | `FloorExposed` | Native |
| Floor | Surface | `FloorInternal` | Native |
| Floor | Adiabatic | `FloorInternal` + `Adiabatic` | Native |
| Floor | Ground / Foundation / Ground\* | `SlabOnGrade` | Native |
| RoofCeiling | Outdoors | `Roof` | Native |
| RoofCeiling | Surface | `Ceiling` | Native |
| RoofCeiling | Adiabatic | `Ceiling` + `Adiabatic` | Native |
| RoofCeiling | Ground / Foundation | `UndergroundCeiling` | Native |
| any | `isAirWall()` / `ConstructionAirBoundary` | `Air` | Native |
| any | `OtherSideCoefficients`, `OtherSideConditionsModel` | `Adiabatic` fallback | Approximated (`SAM-OSI-BC-001`) |
| any | unrecognised | `Undefined` | Unsupported (`SAM-OSI-BC-001`) |

Adjacency: **`Surface.adjacentSurface()` handles are authoritative**. Two paired surfaces
produce **one** SAM `Panel` related to **two** SAM `Space`s. Geometric coincidence matching is
only a validated fallback (`SAM-OSI-ADJ-001` when it is used, `SAM-OSI-ADJ-002` when pairing
fails and the surface is demoted to adiabatic).

### 4.4 SubSurfaces → apertures

| `subSurfaceType` | SAM `ApertureType` | Status | Diagnostic |
|---|---|---|---|
| `FixedWindow` | `Window` | Native | – |
| `OperableWindow` | `Window` (openability not represented) | Approximated | `SAM-OSI-APX-001` |
| `Skylight` | `Window` | Native | – |
| `Door` / `OverheadDoor` | `Door` | Native | – |
| `GlassDoor` | `Door` with transparent panes | Native | – |
| `TubularDaylightDome` / `…Diffuser` | `Window` | Approximated | `SAM-OSI-APX-001` |
| any other | *skipped* | Unsupported | `SAM-OSI-SUB-001` |

`SubSurface.multiplier() > 1` is reported (`SAM-OSI-APX-001`) — SAM has no aperture multiplier.

### 4.5 Shading

| OpenStudio source | SAM target | Status |
|---|---|---|
| `ShadingSurface` in a `Site`/`Building`/`Space` group | `Panel` with `PanelType.Shade` | Native |
| group type + group name | panel metadata | Native |
| `ShadingSurfaceGroup.shadedSurface/shadedSubSurface` | recorded in metadata only | Approximated (`SAM-OSI-APX-001`) |

Group transformations (`ShadingSurfaceGroup.transformation()` composed with the owning space's
`buildingTransformation()` for `Space`-type groups) are applied.

### 4.6 Materials and constructions

| OpenStudio material | SAM material | Status | Diagnostic |
|---|---|---|---|
| `StandardOpaqueMaterial` | `OpaqueMaterial` (+ thickness on the layer) | Native | – |
| `MasslessOpaqueMaterial` | `OpaqueMaterial` derived from R at a nominal 0.025 m | Derived | `SAM-OSI-APX-001` |
| `AirGap` | `GasMaterial` (air) with `HeatTransferCoefficient = 1/R` | Derived | `SAM-OSI-APX-001` |
| `StandardGlazing` | `TransparentMaterial` | Native | – |
| `SimpleGlazing` | `TransparentMaterial` derived from U/SHGC/VT | Approximated | `SAM-OSI-APX-001` |
| `Gas` / `GasMixture` | `GasMaterial` | Native / Approximated | `SAM-OSI-MAT-001` for mixtures |
| `Blind`, `Shade`, `Screen`, `RefractionExtinctionGlazing`, thermochromic, EMS-driven | placeholder layer + report | Unsupported | `SAM-OSI-MAT-001` |

`Construction` → SAM `Construction` (opaque) or `ApertureConstruction` (fenestration), **layer order
preserved outside-first** exactly as the forward direction writes it. `ConstructionAirBoundary`
→ `PanelType.Air`. `ConstructionWithInternalSource`, `CFactorUndergroundWall`,
`FFactorGroundFloor` → `SAM-OSI-CON-001`. Unreferenced materials/constructions are pruned after
conversion, mirroring the Ladybug reverse converter.

### 4.7 Loads, controls and schedules

| OpenStudio source | SAM `InternalCondition` target | Status | Diagnostic |
|---|---|---|---|
| `People` / `PeopleDefinition` | `AreaPerPerson`, `Occupancy*GainPerPerson`, `OccupancyProfileName` | Derived | `SAM-OSI-LOAD-001` |
| `Lights` / `LightsDefinition` | `LightingGain`, `LightingProfileName` | Native | – |
| `ElectricEquipment` | `EquipmentSensibleGain`, profile | Native | – |
| `GasEquipment` | reported | Unsupported | `SAM-OSI-LOAD-001` |
| `SpaceInfiltrationDesignFlowRate` | `InfiltrationAirChangesPerHour` | Derived | `SAM-OSI-LOAD-001` when the design-flow method needs zone data |
| `SpaceInfiltrationEffectiveLeakageArea` | reported | Unsupported | `SAM-OSI-LOAD-001` |
| `DesignSpecificationOutdoorAir` | `SpaceParameter.OutsideSupplyAirFlow` | Derived | – |
| `ThermostatSetpointDualSetpoint` | heating/cooling setpoints + profiles | Native | – |
| `ZoneControlHumidistat` | humidification/dehumidification setpoints | Native | – |
| `SpaceType` | one deduplicated `InternalCondition` per space type | Native | – |
| space-level loads overriding a space type | per-space `InternalCondition` clone | Derived | `SAM-OSI-LOAD-002` |

| OpenStudio schedule | SAM `Profile` | Status | Diagnostic |
|---|---|---|---|
| `ScheduleConstant` | constant profile | Native | – |
| `ScheduleRuleset` (+ `ScheduleDay`, `ScheduleRule`) | annual profile honouring weekday/weekend and seasonal rules | Derived | – |
| `ScheduleYear` / `ScheduleWeek` | annual profile | Derived | – |
| `ScheduleFixedInterval` (hourly) | annual profile | Native | – |
| `ScheduleFixedInterval` (sub-hourly) | hourly means | Approximated | `SAM-OSI-SCH-001` |
| `ScheduleCompact`, `ScheduleFile`, `ScheduleVariableInterval` | reported | Unsupported | `SAM-OSI-SCH-001` |

Profiles not referenced by an imported `InternalCondition` are removed.

---

## 5. Diagnostic codes added

All reverse-only codes live in `SAM.Core.OpenStudio.OpenStudioImportDiagnosticCodes` and use the
`SAM-OSI-*` prefix so they never collide with the forward `SAM-OS-*` codes.

| Code | Meaning |
|---|---|
| `SAM-OSI-IN-001` | Invalid or missing input path |
| `SAM-OSI-IN-002` | Unsupported file extension |
| `SAM-OSI-OSM-001` | OSM could not be loaded |
| `SAM-OSI-OSM-002` | OSM version translation failed or reported errors |
| `SAM-OSI-OSW-001` | OSW could not be parsed |
| `SAM-OSI-OSW-002` | OSW declares no `seed_file` |
| `SAM-OSI-OSW-003` | OSW `seed_file` could not be resolved on disk |
| `SAM-OSI-OSW-004` | Workflow measures were **not** applied (execution disabled) |
| `SAM-OSI-OSW-005` | Workflow execution failed |
| `SAM-OSI-OSW-006` | Final post-model-measure OSM not found |
| `SAM-OSI-OSW-007` | EnergyPlus-measure changes are not representable in the OSM |
| `SAM-OSI-OSW-008` | Workflow executed; final post-model-measure OSM imported |
| `SAM-OSI-OSW-009` | Workflow produced a model but no EnergyPlus results (irrelevant to an import; Information) |
| `SAM-OSI-GEO-001` | Invalid or unconvertible geometry |
| `SAM-OSI-GEO-002` | Duplicate/collinear vertices removed (shape unchanged; Information) |
| `SAM-OSI-ADJ-001` | Adjacency resolved by geometric fallback, not by handle |
| `SAM-OSI-ADJ-002` | Adjacency pairing failed |
| `SAM-OSI-BC-001` | Unsupported outside boundary condition |
| `SAM-OSI-SUB-001` | Unsupported subsurface type |
| `SAM-OSI-MAT-001` | Unsupported material |
| `SAM-OSI-CON-001` | Unsupported construction |
| `SAM-OSI-SCH-001` | Unsupported schedule |
| `SAM-OSI-LOAD-001` | Unsupported load or control |
| `SAM-OSI-LOAD-002` | Conflicting space-type / space-level load assignment |
| `SAM-OSI-HVAC-001` | Detailed HVAC object not imported |
| `SAM-OSI-ZONE-001` | Multi-space thermal zone |
| `SAM-OSI-ZONE-002` | Orphaned space or zone with incomplete geometry |
| `SAM-OSI-ID-001` | Invalid, duplicate or missing SAM identity metadata |
| `SAM-OSI-WEA-001` | Weather/design-day/site limitation |
| `SAM-OSI-SET-001` | Simulation setting without a SAM equivalent |
| `SAM-OSI-APX-001` | Documented approximation applied |

---

## 6. Identity and round-trip metadata

**Audit finding:** the forward exporter currently stores SAM identity **only** in the
deterministic object name `SAM_<Type>_<SanitizedName>_<GuidFirst8>`
(`Core.OpenStudio.Query.OpenStudioName`). A repository-wide search for `additionalProperties`
returns **no hits** — no full SAM GUID is persisted anywhere in the OSM today, and the
eight-character prefix cannot reconstruct a 128-bit GUID.

**Change made (non-breaking):** the forward converters now also write namespaced features onto
every exported object's `AdditionalProperties`:

| Key | Value |
|---|---|
| `SAM.Guid` | full `Guid.ToString("D")` |
| `SAM.Type` | SAM runtime type name |
| `SAM.Name` | original (unsanitised) SAM name |

This adds `OS:AdditionalProperties` objects to the OSM. It changes no existing field, no object
name and no simulation input, so the released `SAMAnalytical.ToOpenStudio` contract is unaffected.

**Reverse behaviour:**

1. A valid full `SAM.Guid` is restored onto the imported SAM object.
2. Otherwise a fresh SAM GUID is created.
3. The OpenStudio handle and original name are preserved as source metadata regardless.
4. `TryGetGuidSuffix` (8 hex chars) is used **only** for matching and diagnostics — never treated
   as a globally unique GUID.
5. An invalid or already-used `SAM.Guid` raises `SAM-OSI-ID-001` and a new GUID is issued.

Third-party OSM files with no SAM metadata import normally; the absence of metadata is not a
diagnostic.

---

## 7. OSW behaviour contract

Parsed with `System.Text.Json`.

| Case | `executeWorkflow` | Behaviour |
|---|---|---|
| Seed OSM present | `false` | Resolve `seed_file` against the OSW directory and `file_paths`; import the seed; **warn** `SAM-OSI-OSW-004` that measures were not applied; `ResolvedOsmPath` = seed. |
| Seed OSM present | `true` | Run the supplied OSW verbatim through the existing CLI runner (steps and arguments untouched); locate the final post-model-measure OSM from `out.osw` / the run directory; import it; preserve every CLI diagnostic. |
| No seed | `false` | Blocking `SAM-OSI-OSW-002` — no model exists to import. |
| No seed | `true` | Allow model-creation measures to produce the model; import the resulting OSM; fail with `SAM-OSI-OSW-006` if none is produced. |

The final OSM is located deterministically (in order): the `out.osw` `"osm_path"`, then
`run/in.osm`, then the newest `*.osm` written under the run directory after the run started.
No unverified hard-coded filename is trusted.

EnergyPlus measures may change only the generated IDF. The returned `AnalyticalModel` represents
the final available **OpenStudio** model; `SAM-OSI-OSW-007` states this whenever the workflow
contains EnergyPlus-measure steps.

---

## 8. Public API

```csharp
namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        // OpenStudio model in memory (caller owns the model)
        public static OpenStudioImportResult ToSAM(this global::OpenStudio.Model model, Core.OpenStudio.OpenStudioImportOptions options = null);

        // OSM or OSW path; native resources are created and disposed inside
        public static OpenStudioImportResult ToSAM(string path, Core.OpenStudio.OpenStudioImportOptions options = null,
            Core.OpenStudio.OpenStudioRunOptions runOptions = null, IProgress<…> progress = null, CancellationToken cancellationToken = default);

        public static Task<OpenStudioImportResult> ToSAMAsync(string path, …);
    }
}
```

`OpenStudioImportResult` exposes `AnalyticalModel`, `SourcePath`, `ResolvedOsmPath`,
`Diagnostics`, `Statistics`, `IsValid`, `Successful`, `OpenStudioVersion`. It **never** exposes a
live native model; models loaded from disk are disposed deterministically after conversion.

---

## 9. Out of scope for this phase

Detailed HVAC (air/plant loops, zone equipment, controls) is reported through
`SAM-OSI-HVAC-001` and not translated. Ground-temperature objects, AirflowNetwork,
daylighting controls, EMS, and refrigeration/water systems are likewise reported, not imported.
