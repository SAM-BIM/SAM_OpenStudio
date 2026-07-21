# OpenStudio → SAM Analytical import — usage and limitations

Status: 2026-07-21, branch `feature/openstudio-to-sam-analytical`.

Companion documents:
[audit and mapping matrix](openstudio-to-sam-import-audit.md) ·
[coverage tables](SAM_OPENSTUDIO_TO_SAM_COVERAGE.md) ·
[forward direction](openstudio-mvp-usage.md).

---

## 1. Grasshopper

**Component:** `OpenStudio.SAMAnalytical` (SAM ▸ OpenStudio), version 1.0.0.

| Input | Required | Meaning |
|---|---|---|
| `_path` | yes | An `.osm` or `.osw` file. |
| `executeWorkflow_` | no, default `false` | OSW only. `false` imports the workflow's **seed** model; `true` runs the workflow and imports its **output**. Ignored for an OSM. |
| `outputDirectory_` | no | Where an executed workflow runs. Defaults to the OSW's own directory. Unused for an OSM. |
| `cancel_` | no | Cancels a running import; the CLI/EnergyPlus process tree is terminated. |

| Output | Meaning |
|---|---|
| `analyticalModel` | The imported SAM `AnalyticalModel` (typed parameter). |
| `resolvedOsmPath` | The OSM actually converted — see §3. |
| `diagnostics` | Every approximation, unsupported object and failure. |
| `successful` | True when a model was produced and no diagnostic has Error severity. |

The component is asynchronous: the canvas stays responsive while a workflow runs, and warnings
and errors also appear as runtime messages on the component itself.

## 2. API

```csharp
using SAM.Analytical.OpenStudio;

// From a path (OSM or OSW). The native model is loaded and disposed inside.
OpenStudioImportResult result = Convert.ToSAM(@"C:\models\office.osm");

// Asynchronous, cancellable — the right choice for an executed workflow.
OpenStudioImportResult result = await Convert.ToSAMAsync(
    @"C:\models\workflow.osw",
    new Core.OpenStudio.OpenStudioImportOptions { ExecuteWorkflow = true },
    cancellationToken: token);

// From a model you already hold. You own the model; the importer never disposes it.
OpenStudioImportResult result = model.ToSAM();

AnalyticalModel analyticalModel = result.AnalyticalModel;
```

`OpenStudioImportResult` carries `AnalyticalModel`, `SourcePath`, `ResolvedOsmPath`,
`OpenStudioVersion`, `RunResult`, `Diagnostics`, `Statistics`, `IsValid` and `Successful`. It is
**not** disposable and never exposes a live native model: a model loaded from disk is released
before the result is returned.

`IsValid` means "a model was produced and nothing errored". `Successful` means "a model was
produced at all" — an import that recovered from errors still returns a usable, incomplete model,
and the two must be distinguishable.

### Options

`Core.OpenStudio.OpenStudioImportOptions` controls tolerances (`DistanceTolerance`,
`AngleTolerance`, `MinimumArea`), what is imported (`IncludeShading`, `IncludeConstructions`,
`IncludeInternalConditions`), whether unpaired interzone surfaces may be matched geometrically
(`AllowGeometricAdjacencyFallback`), whether stamped SAM Guids are restored
(`RestoreSAMIdentity`), workflow execution (`ExecuteWorkflow`, `OutputDirectory`) and library
pruning (`PruneUnreferencedLibraryEntries`).

## 3. OSM versus OSW

**An OSM** is converted directly. The OpenStudio CLI is never invoked, and **no EPW is required**
to import geometry and analytical data. Older files are upgraded on load by the
`VersionTranslator`; the pre-translation version is reported, and translator warnings are
surfaced even on a successful load.

**An OSW is a workflow description, not a building model.** Parsing it tells you what the
workflow *would* produce, never what it *does*. The two cases stay explicit:

| Case | `executeWorkflow_` | Result | `ResolvedOsmPath` |
|---|---|---|---|
| Seed OSM present | `false` | The **seed** is imported. `SAM-OSI-OSW-004` states that the workflow's measures were NOT applied. | the seed |
| Seed OSM present | `true` | The workflow runs verbatim; the **final post-model-measure OSM** is imported. | the final OSM |
| No seed | `false` | Blocking `SAM-OSI-OSW-002` — no model exists to import. | – |
| No seed | `true` | Model-creation measures build the model, which is then imported. | the final OSM |

Seed resolution follows the CLI's own search order — the path as given, `<root>/files`, then each
`file_paths` entry and its `files` subdirectory — and a failure names every location tried.

Executed workflows are staged into a per-import GUID run directory with path fields made absolute
and `run_directory` pinned, so parallel imports of one OSW cannot collide. **Steps and their
arguments are copied verbatim**: the workflow that executes is the one you supplied.

The final OSM is located deterministically — `out.osw`'s recorded `osm_path`, then the CLI's
`run/in.osm`, then the newest OSM written under the run directory *after the run started*. No
unverified filename is trusted, and a stale OSM from an earlier run cannot be mistaken for this
run's output.

## 4. Round-trip identity

The forward exporter stamps `SAM.Guid`, `SAM.Type` and `SAM.Name` into each object's
`OS:AdditionalProperties`. This is additive — a separate OSM object — so no field, object name or
EnergyPlus input changes and the released `SAMAnalytical.ToOpenStudio` contract is unaffected.

On import a valid, unclaimed Guid of the right type is restored. Malformed, duplicated or
mistyped metadata raises `SAM-OSI-ID-001` and a fresh Guid is issued. **A third-party OSM carries
none of this and imports normally** — the absence of metadata is not a diagnostic.

The deterministic name suffix `SAM_<Type>_<Name>_<GuidFirst8>` is 32 bits of a 128-bit Guid. It is
used for matching and diagnostics only and is never treated as a restorable identity.

## 5. What survives a round trip

`AnalyticalModel → OSM → AnalyticalModel` preserves, within declared tolerances: space count and
names; panel count with interzone partitions deduplicated back to one shared panel; panel types
and boundary conditions; adjacency (panel ↔ two spaces); aperture count, type and area; volume
and floor area; construction assignments and layer order; material properties, including an air
cavity's heat transfer coefficient exactly (forward writes `R = 1/h`, reverse reads `h = 1/R`);
internal-condition assignments; schedules as 8760-value profiles; setpoints; location; and full
SAM Guids at model, space, panel and aperture level.

The OSM is not expected to be byte-identical in either direction.

## 6. Limitations

**Reported, not imported.** Detailed HVAC (air loops, plant loops, zone equipment, control
sequences), gas/steam/hot-water/other equipment, luminaires, IT equipment, internal mass,
non-design-flow infiltration, AirflowNetwork, daylighting controls, EMS, refrigeration, and
`ScheduleCompact`/`ScheduleFile`/`ScheduleVariableInterval`. Each raises its diagnostic; none is
silently dropped.

**Approximated, and said so.** `MasslessOpaqueMaterial` (resistance preserved exactly, a near-zero
thermal mass assigned at a nominal 25 mm); `SimpleGlazing` (U preserved, solar transmittance taken
from SHGC, which overstates it); gas mixtures (conductance preserved, composition lost); operable
windows and tubular daylighting devices (geometry and construction preserved, behaviour not);
subsurface multipliers; sub-hourly schedules (averaged to hourly means); design days (name, date
and heating/cooling role only — an EnergyPlus design day is parametric, a SAM `DesignDay` is an
hourly weather day).

**Not embedded.** A `OS:WeatherFile` names an EPW; it does not contain one. The path is recorded
as metadata and no SAM `WeatherData` is created. Supply the EPW separately for annual simulation.

**EnergyPlus measures.** They modify only the generated IDF, not the OpenStudio model, so their
changes cannot appear in the imported SAM model. `SAM-OSI-OSW-007` states this whenever a workflow
has steps.

**Loads on spaces rather than space types.** SAM holds loads on the internal condition only, so
space-level loads are reported (`SAM-OSI-LOAD-002`) and not imported. Likewise, gains are imported
as densities and never resolved against one space's floor area, because a space type is shared.

**Geometry is never repaired.** A boundary that fails validation — too few vertices, non-planar,
self-intersecting, below the minimum area, an aperture outside its host — is reported with the
OpenStudio object's name and handle and skipped.

## 7. Diagnostics

All reverse codes use the `SAM-OSI-*` prefix, disjoint from the forward `SAM-OS-*` codes, so a
consumer can tell which direction produced a diagnostic from the code alone. The full list is in
[`OpenStudioImportDiagnosticCodes`](../SAM_OpenStudio/SAM.Core.OpenStudio/Variables/OpenStudioImportDiagnosticCodes.cs)
and in §5 of the [audit](openstudio-to-sam-import-audit.md); every code is claimed by a coverage
manifest entry, enforced by `ReverseCoverageTests`.
