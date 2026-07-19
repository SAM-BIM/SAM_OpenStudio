# SAM → OpenStudio MVP Implementation Plan

**Status:** Approved for execution
**Last reviewed:** 2026-07-18
**Repository:** `SAM-BIM/SAM_OpenStudio`
**Base branch:** `sow/2026-Q3` (always branch off this; PRs target this)
**Feature branch:** `feature/analytical-model-to-openstudio-mvp`
**Related:** [SAM_Simulation_Engine_Roadmap.md](SAM_Simulation_Engine_Roadmap.md) — this plan executes Roadmap **Phase 1** and the adapter-structure part of **Phase 2**.
**Execution model:** Claude Fable 5 (Max effort) implements the complete MVP in one run; Codex GPT-5.6 Sol (Max effort) reviews independently; see §13.

---

## 1. Technical direction

Implement a direct native conversion using the OpenStudio C# SDK:

```text
SAM.Analytical.AnalyticalModel
            │
            ▼
SAM.Analytical.OpenStudio.Convert.ToOpenStudio(...)
            │
            ▼
OpenStudio.Model.Model
            │
            ├── .osm
            ├── .osw
            ├── EnergyPlus simulation (OpenStudio CLI)
            ├── diagnostics
            └── heating/cooling load results (existing SQL readers)
```

Do **not** implement `SAM → Honeybee → OpenStudio`. `SAM_LadybugTools` is a **semantic reference only** — no Honeybee/Ladybug runtime dependency may be added to this repository. It documents, in working code, how SAM handles:

* geometry preparation and space-facing orientation;
* surface adjacency from the `AdjacencyCluster`;
* forward and reversed constructions;
* aperture conversion;
* material conversion;
* internal-condition deduplication;
* schedules and program types;
* conditioned versus unconditioned spaces.

Reference implementations (read-only), all under
`..\SAM_LadybugTools\SAM_LadybugTools\SAM_LadybugTools.Analytical\Convert\ToLadybugTools\`:

| Concern | File |
| --- | --- |
| Top-level model assembly | `Model.cs` |
| Space → Room, space-facing panels | `Room.cs`, `Face.cs` |
| Boundary conditions incl. adjacency pairing | `BoundaryCondition.cs` |
| Apertures and doors | `Aperture.cs`, `Door.cs` |
| Opaque / massless / gas / glazing materials | `EnergyMaterial.cs`, `EnergyMaterialNoMass.cs`, `EnergyWindowMaterialGas.cs`, `EnergyWindowMaterialGlazing.cs` |
| Constructions (incl. reversed) | `OpaqueConstructionAbridged.cs`, `WindowConstructionAbridged.cs` |
| Internal conditions → program types | `ProgramType.cs` |
| Profiles → schedules | `ScheduleDay.cs`, `ScheduleFixedInterval.cs`, `ScheduleRuleset.cs`, `HourlyContinous.cs` |
| Shading | `Shades.cs` |

---

## 2. Current repository state (audited 2026-07-18)

The implementation extends an **existing** solution — do not scaffold new projects.

| Project | Target | State |
| --- | --- | --- |
| `SAM_OpenStudio\SAM.Core.OpenStudio` | netstandard2.0, x64 | **Exists.** SQL result reading: `Create\SqlFile.cs`, `Create\Model.cs`, `Query\ReportData*.cs`, `Query\AvailableTimeSeriesNames.cs`, `Query\ConvertUnit.cs`, `Classes\ShortDateTime.cs`, `Enums\ReportingFrequency.cs` — **keep and reuse** |
| `SAM_OpenStudio\SAM.Geometry.OpenStudio` | netstandard2.0, x64 | **Exists, empty stub** (`Classes\Test.cs` placeholder — remove during M0 audit) |
| `SAM_OpenStudio\SAM.Analytical.OpenStudio` | netstandard2.0, x64 | **Exists.** Results import: `Convert\ToSAM\SpaceSimulationResults.cs`, `Convert\ToSAM\PanelSimulationResults.cs`, `Create\DesignDays.cs`, `Create\*SimulationResults.cs`, `Query\DesignDays.cs`, `Modify\AddResults.cs`, result-parameter enums — **keep and reuse** |
| `Grasshopper\SAM.Core.Grasshopper.OpenStudio` | net8.0, x64 | **Exists** (Rhino 8) |
| `Grasshopper\SAM.Analytical.Grasshopper.OpenStudio` | net8.0, x64 | **Exists.** Components `OpenStudioCreateDesignDaysBySQL`, `OpenStudioCreateSpaceSimulationResultsBySQL`, `SAMAnalyticalAddResultsBySQL` — **keep** |

Facts that bind the implementation:

1. **Results import already works.** The MVP load-extraction step (§11) reuses the existing SQL layer and `ToSAM_SpaceSimulationResult` path instead of writing a new one.
2. **OpenStudio SDK**: NuGet `OpenStudio` **3.8.0** referenced by both non-Grasshopper libraries. Installed CLI + EnergyPlus is **3.10.0** at `C:\Program Files\ladybug_tools\openstudio` (that bundle also ships C# bindings under `CSharp\`). §11.1 defines the version gate.
3. **Conventions**: static partial classes `Convert` / `Query` / `Create` / `Modify` in folder-per-method layout; output to `..\..\build\`; x64; DLL `HintPath` references to prebuilt sibling repos (`..\..\..\SAM\build\SAM.Core.dll` etc. — all present). Follow these exactly.
4. **Sibling repos are read-only** (`SAM`, `SAM_LadybugTools`, `SAM_SQLite`). Consume SAM DLLs from `SAM\build\`; never edit or rebuild siblings as part of this work.
5. **No test project exists** — created in M0 (`tests/`, net8.0, x64, NUnit unless an ecosystem convention dictates otherwise).
6. **No SPDX headers exist** in current sources — add SPDX headers to **new files only**; do not churn existing files.
7. **No EPW files exist** anywhere in the workspace — see §11.3 weather fixture policy.
8. Workspace root on this machine: `C:\Users\Virtual Machine\Documents\GitHub\SAM-BIM`.

Naming alignment with the Roadmap (§6.2 of the roadmap): this plan's `OpenStudioConversionOptions` / `OpenStudioConversionResult.Diagnostics` / `OpenStudioSimulationRunner` fulfil the roadmap's `OpenStudioTranslationOptions` / `OpenStudioTranslationReport` / `OpenStudioSimulationRunner` intent. The diagnostics list serialised to JSON **is** the translation report.

---

## 3. MVP definition

### Input

```csharp
AnalyticalModel analyticalModel
string epwPath
OpenStudioConversionOptions options
```

### Output

```csharp
OpenStudioConversionResult
```

containing:

```csharp
OpenStudio.Model.Model Model
string OsmPath
string OswPath
OpenStudioRunResult RunResult
IReadOnlyList<OpenStudioDiagnostic> Diagnostics
IReadOnlyDictionary<Guid, string> ObjectMap
bool IsValid
```

### MVP capabilities

1. SAM spaces and thermal-zone geometry.
2. External and internal panels.
3. Surface adjacency.
4. Windows and doors.
5. Opaque, transparent, and gas materials.
6. Opaque and aperture constructions.
7. Occupancy, lighting and equipment gains.
8. Infiltration and outdoor-air requirements.
9. Heating and cooling setpoint schedules.
10. OpenStudio thermal zones.
11. Ideal Loads HVAC.
12. EPW weather assignment.
13. Annual EnergyPlus simulation via the OpenStudio CLI.
14. Heating and cooling load extraction **reusing the existing SQL readers** (`SAM.Core.OpenStudio` `SqlFile`/`ReportData`; `SAM.Analytical.OpenStudio` `ToSAM_SpaceSimulationResult`).
15. Detailed conversion and simulation diagnostics.

### Explicitly deferred

* SAM AirSystem conversion; detailed HVAC plant and air loops; water systems; controls;
* daylight simulation; natural ventilation algorithms; dynamic glazing; complex shading controls; moisture transport;
* results import **richer than** the existing `SpaceSimulationResult`/`PanelSimulationResult` path;
* automatic repair of seriously invalid SAM geometry.

---

## 4. Definition of success

The MVP is complete when a representative `AnalyticalModel` can:

1. convert to an OpenStudio `Model`;
2. save a valid `.osm`;
3. generate an `.osw`;
4. run through the OpenStudio CLI (3.10.0 at the ladybug_tools path or a discovered install);
5. complete EnergyPlus with no fatal errors;
6. produce heating and cooling results satisfying the M6 gate (§13): annual heating ≥ 0 **and** annual cooling ≥ 0 **and** at least one > 0 for the conditioned fixture; zone-level result count equals conditioned-zone count; SQL units validated and documented;
7. preserve SAM floor area and volume within agreed tolerances;
8. preserve internal surface relationships;
9. preserve construction layer order;
10. reproduce SAM schedules and internal gains;
11. return actionable diagnostics rather than silently substituting data.

---

## 5. Object mapping

| SAM object | OpenStudio object | MVP rule |
| --- | --- | --- |
| `AnalyticalModel` | `Model` | One OpenStudio model |
| `AdjacencyCluster` | model geometry graph | Primary topology source |
| `Space` | `Space` | One-to-one |
| `Space` | `ThermalZone` | One thermal zone per SAM space |
| SAM level/elevation | `BuildingStory` | Group by floor elevation tolerance |
| `Panel` | `Surface` | One surface for each space-facing side |
| Internal `Panel` | two matched `Surface` objects | One outward-facing surface per space |
| External `Panel` | `Surface` | Outdoors, Ground or Adiabatic |
| `Aperture` | `SubSurface` | Window, door or glass door |
| Shading panel | `ShadingSurface` | Optional MVP extension |
| `OpaqueMaterial` | `StandardOpaqueMaterial` | Preserve physical properties |
| Massless material | `MasslessOpaqueMaterial` | Where SAM data supports it |
| `TransparentMaterial` | glazing material | Detailed glazing where properties exist |
| Aggregate glazing | `SimpleGlazingSystem` | Controlled fallback only |
| `GasMaterial` | `Gas` | Preserve type and thickness |
| `Construction` | `Construction` | Ordered material layers |
| `ApertureConstruction` | `Construction` | Pane layers initially |
| `InternalCondition` | `SpaceType` or direct space loads | Deterministically deduplicated |
| Occupancy profile | `People` and schedule | Preserve density and profile |
| Lighting profile | `Lights` and schedule | Preserve W/m² where available |
| Equipment profile | `ElectricEquipment` and schedule | Preserve W/m² where available |
| Infiltration | `SpaceInfiltrationDesignFlowRate` | Use explicit SAM basis |
| Ventilation | `DesignSpecificationOutdoorAir` | Per-person/per-area values |
| Heating profile | heating setpoint schedule | Applied to thermostat |
| Cooling profile | cooling setpoint schedule | Applied to thermostat |
| Conditioned space | `ZoneHVACIdealLoadsAirSystem` | MVP load calculation |
| Unconditioned space | no Ideal Loads system | Geometry and gains retained |
| SAM weather/location | site and EPW | EPW is explicit input |

---

## 6. Code architecture (additions to the existing projects)

### `SAM.Core.OpenStudio` — add

```text
Classes/
    OpenStudioDiagnostic.cs
    OpenStudioDiagnosticSeverity.cs
    OpenStudioConversionOptions.cs
    OpenStudioRunOptions.cs
    OpenStudioRunResult.cs
    OpenStudioObjectReference.cs
    OpenStudioObjectMap.cs

Query/
    SanitizeName.cs
    OpenStudioVersion.cs
    OpenStudioCliPath.cs      // discovery order — see §11.2
```

Diagnostic shape (note the enum type is `OpenStudioDiagnosticSeverity` — one name, used consistently):

```csharp
public sealed class OpenStudioDiagnostic
{
    public string Code { get; }
    public OpenStudioDiagnosticSeverity Severity { get; }
    public string Message { get; }
    public Guid? SamGuid { get; }
    public string SamObjectType { get; }
    public string OpenStudioObjectName { get; }
}
```

Diagnostic codes:

```text
SAM-OS-GEO-001   Invalid or non-planar boundary
SAM-OS-GEO-002   Duplicate or collinear vertices removed
SAM-OS-ADJ-001   Missing adjacent surface
SAM-OS-MAT-001   Unsupported material
SAM-OS-CON-001   Missing construction layer
SAM-OS-SCH-001   Missing profile
SAM-OS-IC-001    Unsupported internal-condition parameter
SAM-OS-HVAC-001  Conditioned zone missing setpoints
SAM-OS-RUN-001   OpenStudio CLI failed
SAM-OS-EPLUS-001 EnergyPlus severe error
```

### `SAM.Geometry.OpenStudio` — add (remove `Classes\Test.cs` placeholder)

```text
Convert/
    ToOpenStudioPoint3d.cs
    ToOpenStudioPoint3dVector.cs
    ToOpenStudioPolygon.cs

Query/
    CleanVertices.cs
    IsPlanar.cs
    SignedArea.cs
    IsClockwise.cs
    Normal.cs
    ValidatePolygon.cs
```

Geometry primitives only — this project must not know about spaces, materials, schedules or simulations.

### `SAM.Analytical.OpenStudio` — add (alongside the existing `Convert\ToSAM\`, `Create\`, `Query\`, `Modify\`)

```text
Classes/
    OpenStudioConversionContext.cs
    OpenStudioConversionResult.cs
    OpenStudioModelBuilder.cs
    OpenStudioSimulationRunner.cs
    OpenStudioLoadSummary.cs

Convert/
    ToOpenStudio/
        AnalyticalModel.cs
        BuildingStory.cs
        Space.cs
        ThermalZone.cs
        Panel.cs
        Aperture.cs
        Material.cs
        Construction.cs
        Profile.cs
        InternalCondition.cs
        SpaceLoads.cs
        Thermostat.cs
        IdealLoads.cs
        Weather.cs
        SimulationSettings.cs

Query/
    OutsideBoundaryCondition.cs
    SurfaceType.cs
    SubSurfaceType.cs
    IsConditioned.cs
    ConstructionOrientation.cs
    ProfileValues.cs
```

### `SAM.Analytical.Grasshopper.OpenStudio` — add (keep existing SQL components)

```text
AnalyticalModelToOpenStudio
RunOpenStudioModel
OpenStudioLoadResults
```

Thin components only: no conversion rules in Grasshopper code.

### `tests/` — new

```text
tests/
    SAM.Analytical.OpenStudio.Tests/    // net8.0, x64, NUnit (confirm at M0)
    resources/
        weather/                        // pinned EPW + DDY, see §11.3
        fixtures/                       // serialized SAM fixture models
```

---

## 7. Conversion context and naming

Every mapper receives the same context:

```csharp
public sealed class OpenStudioConversionContext
{
    public AnalyticalModel Source { get; }
    public OpenStudio.Model.Model Target { get; }
    public OpenStudioConversionOptions Options { get; }

    public IDictionary<Guid, ModelObject> ObjectMap { get; }
    public IDictionary<Guid, Surface> PrimarySurfaceMap { get; }
    public IDictionary<string, Material> MaterialMap { get; }
    public IDictionary<string, Construction> ConstructionMap { get; }
    public IDictionary<Guid, Schedule> ScheduleMap { get; }

    public IList<OpenStudioDiagnostic> Diagnostics { get; }
}
```

This prevents repeated conversion, duplicate objects, unstable naming, difficult reference resolution, and warnings lost inside extension methods.

Deterministic names, never OpenStudio-generated handles:

```text
SAM_<ObjectType>_<SanitizedName>_<GuidFirst8>

SAM_Space_Office_04_72a6f932
SAM_Surface_Wall_External_b42bd811
SAM_Construction_ExtWall_2015_195f1c74
```

---

## 8. Geometry strategy

### Coordinates

* retain SAM global coordinates; metres; no local space transformations;
* all OpenStudio space origins at zero; preserve north separately at building level.

### Vertex preparation (before creating any surface)

1. remove consecutive duplicate vertices;
2. remove duplicate closing vertex;
3. remove collinear vertices within tolerance;
4. confirm at least three valid vertices;
5. confirm planarity;
6. check polygon area;
7. orient vertices relative to the SAM space;
8. record every modification as a diagnostic.

Do not triangulate. Do not auto-repair non-planar polygons — reject with a diagnostic. All tolerances come from `OpenStudioConversionOptions`.

### Internal panels

```text
Space A → Surface A
Space B → Surface B
Surface A.adjacentSurface = Surface B
Surface B.adjacentSurface = Surface A
```

Reversed vertex order on the opposite side (each normal points out of its own space). **SAM adjacency is the source of truth** — OpenStudio geometric matching may be used only as a validation check. This mirrors the LadybugTools approach (`Room.cs` + `BoundaryCondition.cs`).

### Two passes

* **Pass 1 — create**: BuildingStories, Spaces, ThermalZones, Surfaces, SubSurfaces.
* **Pass 2 — resolve**: adjacent surfaces/subsurfaces, boundary conditions, constructions, thermal zones, shading groups.

Never resolve references while only half the objects exist.

---

## 9. Construction orientation

Layer convention to verify against SAM (do not assume): `external side → internal side`.

* External panel → forward construction.
* Internal panel: Surface A → forward; Surface B → reversed.
* Create and cache reversed constructions only when required, keyed:

```text
ConstructionGuid + ":Forward"
ConstructionGuid + ":Reverse"
```

`SAM_LadybugTools` generates both directions (`OpaqueConstructionAbridged.cs`) — read it before implementing, and document SAM's actual layer-order convention in the material mapping doc before coding M4.

---

## 10. Internal conditions, profiles, schedules

For each unique `InternalCondition`: retrieve scalar parameters and referenced profiles (by `ProfileType` from the model `ProfileLibrary`), convert and cache schedules, create a `SpaceType` with people/lights/equipment definitions plus ventilation and infiltration, assign to every space using that condition; thermostats are assigned per thermal zone.

**Before coding**, author `docs/SAM_OPENSTUDIO_INTERNAL_CONDITION_MAPPING.md`; for each parameter record: SAM parameter, SAM unit, OpenStudio object, OpenStudio field, conversion formula, missing-value policy, validation range, MVP status. **Units must come from SAM source inspection, never inferred from parameter names.** Same requirement for `docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md` before M4.

### Schedule policy (MVP)

```text
SAM Profile → expanded annual values → OpenStudio ScheduleFixedInterval
```

* preserve source timestep; explicitly handle leap years; validate expected value counts;
* assign schedule type limits; cache by profile GUID + semantic usage;
* never silently replace a missing profile with AlwaysOn — diagnostic `SAM-OS-SCH-001`.
* `ScheduleRuleset` compression is a post-MVP optimisation.

---

## 11. Simulation strategy and toolchain

For each conditioned zone: `ThermostatSetpointDualSetpoint` + `ZoneHVACIdealLoadsAirSystem`. Unconditioned/external zones: thermal zone only, no thermostat, no Ideal Loads. The conditioned-space decision matches existing SAM behaviour (including current treatment of internal-condition names containing `unconditioned`/`external`), centralised in one `Query.IsConditioned`.

Requested outputs (verify exact names against the EnergyPlus bundled with the selected CLI):

```text
Zone Ideal Loads Supply Air Total Heating Energy
Zone Ideal Loads Supply Air Total Cooling Energy
Zone Ideal Loads Supply Air Sensible Heating Energy
Zone Ideal Loads Supply Air Sensible Cooling Energy
Zone Mean Air Temperature
Zone Operative Temperature
```

Run result:

```csharp
public sealed class OpenStudioRunResult
{
    public bool Success { get; }
    public int ExitCode { get; }
    public string OsmPath { get; }
    public string OswPath { get; }
    public string SqlPath { get; }
    public string ErrorFilePath { get; }
    public IReadOnlyList<string> SevereErrors { get; }
    public IReadOnlyList<string> FatalErrors { get; }
    public OpenStudioLoadSummary Loads { get; }
}
```

Runner requirements: EPW validation, site from EPW, annual `RunPeriod`, timestep, documented MVP shadow/convection defaults, optional DDY design-day import, OSM save, OSW generation, CLI process execution with timeout and cancellation, run-directory isolation, `eplusout.err` parsing, SQL discovery, structured result. No Rhino/Grasshopper dependency.

### 11.1 SDK/CLI version gate (decided at M0, recorded in the audit doc)

Current state: NuGet SDK **3.8.0**; installed CLI+EnergyPlus **3.10.0** (`C:\Program Files\ladybug_tools\openstudio`, bundled C# bindings in `CSharp\`).

1. **Preferred**: bump NuGet to `OpenStudio 3.10.0` (if published) so SDK matches CLI; rebuild existing results code; run smoke test.
2. **Fallback**: stay on 3.8.0 and rely on the CLI's VersionTranslator (a newer CLI opens an older OSM; the reverse is not true). The M0 smoke test must prove the 3.8.0-SDK-generated OSM round-trips through the 3.10.0 CLI.
3. Local `CSharp\` bindings from the ladybug_tools bundle are a last resort only (hurts CI reproducibility).

### 11.2 CLI discovery order (implemented in `Query.OpenStudioCliPath`)

1. Explicit path in `OpenStudioRunOptions`;
2. direct OpenStudio installation (`openstudio` on PATH, then standard install locations);
3. `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` (present on this machine, v3.10.0).

Missing CLI → structured error `SAM-OS-RUN-001` naming the paths searched — never a silent skip.

### 11.3 Weather fixture policy (pinned at M0)

* Location: `tests/resources/weather/` — one EPW + matching DDY (official energyplus.net source; e.g. London Gatwick, ~1.5 MB).
* The audit doc records: exact EPW URL, exact DDY URL, SHA-256 checksums, source and licence, and whether files are committed to the repo (default) or fetched by a setup script.
* Tests must never depend on a mutable external file.

---

## 12. Validation fixtures and semantic assertions

| Fixture | Purpose |
| --- | --- |
| Single-zone box | Basic geometry and annual run |
| Two adjacent boxes | Internal surface matching |
| Box with window and door | SubSurface conversion |
| Two-level model | BuildingStory assignment |
| Multilayer external wall | Material and layer order |
| Glazed façade | Transparent and gas materials |
| Occupied office | People, lights and equipment |
| Conditioned/unconditioned pair | HVAC assignment rules |
| Irregular planar room | Polygon cleaning |
| Invalid non-planar panel | Diagnostics and failure policy |

Never compare `.osm` byte-for-byte (handles change). Assert semantics:

```text
space count · thermal-zone count · surface count · subsurface count
matched-surface count · material count · construction count
floor area · volume · construction layers · schedule values
load densities · setpoint values · weather file · run period
simulation exit code · fatal/severe error count · heating and cooling results
```

Plus: conversion-report snapshots, diagnostic-code assertions, deterministic-name assertions, proof that repeated conversion does not mutate the source `AnalyticalModel`, and memory/disposal checks for repeated OpenStudio model creation.

---

## 13. Execution model

### Five stages, cross-vendor review

```text
Stage 1  Claude Fable 5 (Max)      Complete MVP in ONE run — milestones M0–M8, commit per gate
Stage 2  same session              git push -u origin feature/analytical-model-to-openstudio-mvp
Stage 3  Codex GPT-5.6 Sol (Max)   Full independent technical review — findings only, no edits
Stage 4  Claude Fable 5 (High/Max) Fix confirmed P0/P1 findings — one commit + regression test each
Stage 5  Claude Fable 5 (Medium)   Final validation: full suite + ≥3 EnergyPlus runs + Rhino 8 smoke test → PR into sow/2026-Q3
```

| Stage | Tool | Model | Effort | Output |
| --- | --- | --- | --- | --- |
| 1. MVP in one run (M0–M8) | Claude Code, fresh session | **Claude Fable 5** | **Max** | Converter + tests + docs; commit per milestone |
| 2. Push | same session | — | — | Feature branch on origin |
| 3. Independent review | **Codex CLI** | **GPT-5.6 Sol** | **Max** | `docs/openstudio-mvp-review.md`, P0–P3 findings; review-only |
| 4. P0/P1 fixes | Claude Code (Sol acceptable) | Fable 5 | High (Max for geometry/schedule semantics) | One commit per finding + regression test |
| 5. Validation + PR | Claude Code | Fable 5 | Medium | Suite + ≥3 simulations + Rhino 8 smoke test; PR |

Optional supplement after Stage 4: `/code-review ultra <PR#>` (Claude multi-agent cloud review; user-triggered).

Rationale: implementer and reviewer come from different model families — defect-class diversity, with the implementer holding the entire conversion chain in one context. Milestones replace the original 14 phase-gated prompts: they preserve reviewability, bisectability and resumability without breaking the agent's end-to-end understanding.

### Stage-1 milestones

Every milestone ends with: **solution builds + all tests pass + commit + update of `docs/openstudio-mvp-status.md`**. The status file records: current milestone completed, commit SHA, tests executed, known limitations, next milestone, SDK and CLI versions selected, and the commands needed to resume. This protects the one-run strategy against context, usage or machine limits — any fresh session resumes from the last gate.

| M | Scope | Exit gate |
| --- | --- | --- |
| **M0** | Audit existing code (keep/reuse map); SDK/CLI version gate (§11.1); CLI discovery (§11.2); smoke test: instantiate `Model` + `Space` + `ThermalZone` → save OSM → reload → installed CLI opens it, outside Rhino; create `tests/` project; pin weather fixture (§11.3); write `docs/openstudio-mvp-audit.md`; create `docs/openstudio-mvp-status.md` | Smoke test green including CLI round-trip |
| **M1** | Contracts: options, diagnostics, severity, object map, context, result, deterministic naming | Naming/dedup/diagnostic-aggregation/IsValid tests |
| **M2** | Geometry primitives: point/vector/polygon conversion, vertex cleaning, planarity, area, orientation validation | 9 cases: floor, wall, sloped roof, reversed, duplicate vertices, collinear, too-few, non-planar, near-zero area |
| **M3** | Stories, spaces, zones; surfaces in passes A/B/C (create → space-facing panels via `AdjacencyCluster` → explicit adjacency + boundary conditions); apertures/doors/shading (`SubSurface` types, host containment validation, paired internal subsurfaces, optional edge offset per LadybugTools rationale) | Two-box fixture: counts, area/volume vs SAM, paired reversed normals, Outdoors/Ground/Adiabatic/Surface assignment; aperture-in-host tests |
| **M4** | `SAM_OPENSTUDIO_MATERIAL_MAPPING.md` first; opaque/massless/gas/glazing materials (GUID-deduplicated, validated physical values, no hidden defaults); constructions with layer resolution via `MaterialLibrary`, forward/reverse caching, air-boundary handling | Every mapped field unit-tested; layer sequence proven correct on **both** sides of an internal panel |
| **M5** | `SAM_OPENSTUDIO_INTERNAL_CONDITION_MAPPING.md` first; profiles → `ScheduleFixedInterval` (timestep, leap years, type limits, caching); `SpaceType` per unique `InternalCondition`; People/Lights/ElectricEquipment (+definitions), infiltration, outdoor air | Annual-value spot checks across the year; SpaceType dedup (two spaces share one); missing profile → error not AlwaysOn |
| **M6** | Central `IsConditioned`; dual-setpoint thermostats; Ideal Loads per conditioned zone; EPW/site/RunPeriod/timestep/outputs; OSM+OSW save; CLI runner (timeout, isolation, err parsing, SQL discovery); wire existing SQL readers for load extraction | **First E2E** on one-zone fixture: exit 0; no fatal/severe errors; annual heating ≥ 0 AND cooling ≥ 0 AND at least one > 0; zone-level result count == conditioned-zone count; SQL units validated and documented. Zeros-only output **fails** this gate |
| **M7** | Full 10-fixture regression suite (§12) with semantic assertions; no-source-mutation and disposal checks | All fixtures green |
| **M8** | Thin Grasshopper components (`AnalyticalModelToOpenStudio`, `RunOpenStudioModel`, `OpenStudioLoadResults`); native-DLL packaging for net8; workflow docs; self-check against §4 | Solution + components build; MVP checklist table completed in status file |

### Mandatory pre-PR gate (Stage 5, human-assisted) — Rhino 8 smoke test

```text
Rhino 8 starts
Grasshopper loads the GHA
The OpenStudio components appear
A one-zone model converts without native-DLL loading errors
```

A solution build alone does not prove x64 native deployment — this is one of the largest project risks. Comprehensive Rhino 8/9 testing remains follow-up after the PR.

---

## 14. Stage-1 implementation brief (base prompt for the fresh Fable 5 Max session)

```text
You are a senior C# building-performance engineer implementing the SAM →
OpenStudio MVP in one continuous run.

Read first:
  docs/SAM_OpenStudio_MVP_Implementation_Plan.md   (this plan — binding)
  docs/SAM_Simulation_Engine_Roadmap.md            (strategic context)

Workspace: C:\Users\Virtual Machine\Documents\GitHub\SAM-BIM
Primary repository: SAM_OpenStudio (branch feature/analytical-model-to-openstudio-mvp,
branched off sow/2026-Q3).
Reference repositories (READ-ONLY): SAM, SAM_LadybugTools, SAM_SQLite.
SAM assemblies are consumed prebuilt from SAM\build\ via existing HintPath references.

Target: native conversion SAM.Analytical.AnalyticalModel → OpenStudio.Model.Model,
sufficient to run an EnergyPlus Ideal Loads simulation and extract heating/cooling
loads via the existing SQL readers.

Architecture rules:
1.  SAM_LadybugTools is a semantic reference only — no Honeybee/Ladybug dependency.
2.  Use the OpenStudio C# SDK directly (version per the M0 gate, plan §11.1).
3.  Generic infrastructure in SAM.Core.OpenStudio; geometry primitives in
    SAM.Geometry.OpenStudio; analytical conversion in SAM.Analytical.OpenStudio;
    Grasshopper components thin.
4.  Follow existing repo conventions: static partial Convert/Query/Create/Modify
    classes, one file per method group, netstandard2.0, x64, output ..\..\build\.
5.  SAM AdjacencyCluster relationships are the topology source of truth.
6.  Preserve SAM GUID traceability and deterministic object names (plan §7).
7.  Never silently substitute missing materials, constructions, profiles or
    internal-condition values — structured diagnostics (plan §6 codes).
8.  Reuse the existing results-import code; do not duplicate it.
9.  SPDX headers on NEW files only; preserve existing public APIs unless a change
    is explicitly justified in the audit doc.
10. No detailed HVAC. No modification of sibling repositories or master.
11. Units from SAM source inspection only — write the two mapping docs before
    coding M4 and M5.

Working method:
- Complete milestones M0–M8 in order (plan §13). At each gate: build the full
  solution, run all tests, commit (conventional message, plan §16), and update
  docs/openstudio-mvp-status.md (milestone, commit SHA, tests executed, known
  limitations, next milestone, SDK+CLI versions, resume commands).
- Inspect existing SAM implementations and the LadybugTools reference (plan §1
  table) before writing each subsystem; document assumptions before coding.
- Stop only on a hard blocker (record it in the status and audit docs) or when
  the §4 definition of success is fully satisfied.
- Finish by pushing the branch (Stage 2) and reporting: milestones completed,
  commits, test counts, E2E simulation evidence, known limitations, risks.
```

---

## 15. Stage-3 independent review brief (Codex GPT-5.6 Sol, Max effort)

```text
Act as an independent senior reviewer of the SAM → OpenStudio MVP on branch
feature/analytical-model-to-openstudio-mvp (SAM_OpenStudio repository).
You must NOT implement, edit, or commit code. Findings only.

Read docs/SAM_OpenStudio_MVP_Implementation_Plan.md for the binding contract,
then review the milestone commits individually for scoped context.

Review for:
- incorrect SAM semantics; geometry orientation defects; incorrect internal
  adjacency; construction layer reversal; unit conversion errors; schedule
  calendar errors (timestep, leap years); incorrectly conditioned spaces;
- misuse of OpenStudio object ownership or lifetimes; native DLL deployment
  risks (netstandard2.0 libraries and the net8 Grasshopper assembly);
- mutation of the source AnalyticalModel; silent fallback behaviour;
  missing validation; nondeterministic tests; excessive Grasshopper coupling;
- SDK 3.8.0/CLI 3.10.0 version-translation risks if the M0 gate chose mixed
  versions.

Verification you must run:
- build the full solution;
- run the complete test suite;
- run at least three complete EnergyPlus simulations from the fixture set and
  check the M6 result criteria (plan §13).

Produce docs/openstudio-mvp-review.md classifying every finding:
  P0 blocking · P1 required before merge · P2 follow-up · P3 optional
with file/line references and reproduction evidence. Do not expand MVP scope.
```

Stage 4 (separate session, Claude Fable 5 High — Max for geometry/schedule semantics): fix confirmed P0/P1 findings only, one commit per finding, each with a regression test. Stage 5: full suite, ≥3 simulations, Rhino 8 smoke test (§13), then PR into `sow/2026-Q3`.

---

## 16. Branch and commit structure

```text
base:    sow/2026-Q3          (always; also the PR target)
feature: feature/analytical-model-to-openstudio-mvp
```

One commit per milestone (each must build and pass all tests introduced so far):

```text
M0  docs: audit OpenStudio toolchain and existing conversion code
M1  feat: add OpenStudio conversion contracts and diagnostics
M2  feat: convert SAM geometry primitives to OpenStudio
M3  feat: convert spaces zones stories surfaces and apertures
M4  feat: convert analytical materials and constructions
M5  feat: convert profiles internal conditions and space loads
M6  feat: assign thermostats ideal loads and add simulation runner
M7  test: add SAM OpenStudio MVP regression fixtures
M8  feat: expose OpenStudio conversion in Grasshopper and document workflow
```

Stage-4 fix commits: `fix: <finding> (review P0/P1-<n>)` each with its regression test.

---

## 17. Estimates, risks, follow-ups

* Stage 1 ≈ one long working session (single run). Calendar ≈ **2–4 days** including Codex review turnaround and the fix round — replacing the original 25–36 engineering-day phased estimate.
* Highest risks: SDK/CLI version gate outcome (§11.1); x64 native deployment into Rhino 8 (mitigated by the mandatory smoke test); SAM internal-condition units (mitigated by mapping docs); profile calendar semantics (leap years, timestep).
* Follow-on projects (unchanged): AirSystem/detailed HVAC (Roadmap Phase 5), TAS-vs-OpenStudio validation harness (Roadmap Phase 3), AI-assisted QA (Roadmap Phase 4), richer results import, `ScheduleRuleset` compression.
