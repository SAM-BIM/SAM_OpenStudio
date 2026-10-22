# SAM → OpenStudio material and construction mapping

**Status:** Binding for the MVP (M4). Units verified against SAM sources and the
SAM_LadybugTools/SAM_Tas converters — never inferred from parameter names.

## Layer-order convention (verified)

SAM's own documentation (`SAM.Analytical\Classes\Construction.cs`, `ApertureConstruction.cs`,
`Query\Materials.cs`) states:

> "Order of materials from inside to outside following the TAS approach."

**SAM `ConstructionLayers[0]` is the INNERMOST layer; the last layer is OUTERMOST.**
EnergyPlus/OpenStudio constructions list layers **outside first** ("Outside Layer").

Therefore:

| Variant | Cache key | Layer order | Used for |
| --- | --- | --- | --- |
| Forward | `<ConstructionGuid>:Forward` | reversed SAM list (outside → inside) | every external/ground/adiabatic surface; the primary side of an internal pair |
| Reverse | `<ConstructionGuid>:Reverse` | SAM list as stored (inside → outside) | the secondary side of an internal pair |

The two sides of an internal panel therefore always carry exact layer reversals of each
other (EnergyPlus requirement for matched surfaces). Cross-check: SAM_Tas feeds
`ConstructionLayers` to TAS unchanged (TAS shares SAM's convention); SAM_LadybugTools
reverses for its default/external variant, consistent with honeybee's outside→inside order.

Layer thickness comes from **`ConstructionLayer.Thickness`** (as in SAM_Tas), not from the
material's `DefaultThickness`; the material default is only a fallback (warning diagnostic)
when the layer thickness is missing or non-positive. One OpenStudio material instance is
created per **(SAM material Guid, thickness)** pair.

## OpaqueMaterial → OS:Material (StandardOpaqueMaterial)

| SAM source | Unit | OpenStudio field | Conversion | Missing-value policy |
| --- | --- | --- | --- | --- |
| `ConstructionLayer.Thickness` | m | Thickness | direct | fallback `MaterialParameter.DefaultThickness` (warning); error if still invalid |
| `ThermalConductivity` | W/m·K | Conductivity | direct | **error** SAM-OS-MAT-001 (≤0/NaN invalid) |
| `Density` | kg/m³ | Density | direct | **error** (≤0/NaN invalid) |
| `SpecificHeatCapacity` | J/kg·K | Specific Heat | direct | **error** (≤0/NaN invalid) |
| — | — | Roughness | fixed `MediumSmooth` | LadybugTools parity |
| `OpaqueMaterialParameter.ExternalEmissivity` | 0–1 | Thermal Absorptance | direct | warning; OpenStudio default 0.9 |
| `OpaqueMaterialParameter.ExternalSolarReflectance` | 0–1 | Solar Absorptance | `1 − x` | warning; OpenStudio default 0.7 |
| `OpaqueMaterialParameter.ExternalLightReflectance` | 0–1 | Visible Absorptance | `1 − x` | warning; OpenStudio default 0.7 |

Validation ranges: conductivity > 0; density > 0; specific heat > 0; thickness > 0;
absorptances within [0, 1] (out-of-range → error, no clamping).

### Opaque face optics (SAM_OpenStudio#33 revision 8)

EnergyPlus opaque materials hold **one** absorptance set for both faces, and the heat balance
uses only the outermost layer's outside face (outdoor surfaces) and the innermost layer's
zone face. SAM stores layers inside → outside from the panel's first space in
`AdjacencyCluster.GetSpaces()` order (S07 section 1), so:

| Face EnergyPlus uses | SAM layer | SAM face |
| --- | --- | --- |
| zone face, first space (and every single-sided surface) | first | `Internal*` |
| zone face, second space of an internal panel | last | `External*` |
| outside face of an outdoor surface | last | `External*` |

- **Rule.** SAM's first layer of an opaque construction takes its `Internal*` optics; every
  other layer takes its `External*` optics. Where a material's `Internal*` values differ
  from `External*`, the first layer uses an internal-face variant: its own cache key
  `(Guid, thickness, Internal)` and name suffix `_Internal`, thermally identical to the
  external-face object. The face is chosen by SAM layer position, never by EnergyPlus position, so
  forward and reverse constructions share the same objects and stay exact mirrors.
- **Inherited zone-face values.** An `Internal*` value SAM does not state (absent or NaN) on a
  zone face takes the stated `External*` value. This is reported once per material as
  **information SAM-OS-MAT-004 at Summary**; the written value is not stamped as authored, so
  an import keeps the internal value unspecified. S07 lists no default for an internal face,
  so S09 reports those zone-face rows as S-UNSPEC EXPORT-DEFECT, never as a match.
- **Single-layer constructions** (one layer is both faces): agreeing faces are written
  normally; on the ground or an adiabatic boundary only the zone face is used, so the
  internal values are written. On an outdoor or interzone surface (including a two-sided
  panel whose pairing failed) differing faces are **error SAM-OS-MAT-003**: no construction
  is assigned and the conversion is unsuccessful — no external, averaged or split-layer
  substitute. The check runs per panel use, before any cached construction is reused.
- **Orientation.** The forward construction goes to the first space's side only. If that
  side produced no surface the panel gets **error SAM-OS-ADJ-001** and no construction; the
  sides are never swapped.
- **Opaque door panes** (opaque layers of a fenestration construction) are unchanged: the
  external values govern both faces, SAM-OS-MAT-002 information when they differ.
- **Import identity.** Every opaque object written from a SAM opaque material carries, in
  `OS:AdditionalProperties`, `SAM.MaterialGuid` (source material), `SAM.MaterialFace`
  (`External`/`Internal`), each authored `SAM.Optic.<OpaqueMaterialParameter>` SAM states, and
  `SAM.Written.ThermalAbsorptance` / `.SolarAbsorptance` / `.VisibleAbsorptance` (what was
  actually written, including inherited values and OpenStudio defaults).
  - **Unchanged objects** restore exactly the authored values on both faces; an unstated value
    stays unstated (an unchanged OpenStudio default is not imported as SAM data).
  - **Edited objects** (an absorptance differing from its written value) keep the edited value
    on every SAM face the object supplies and report it (warning SAM-OSI-MAT-001), so an edit
    of an unstated value is not lost on re-export. An `Internal` variant supplies the zone face;
    an `External` object of a material whose authored faces differ supplies the external face
    (the variant supplies the zone face). An `External` object of a material whose faces agree
    is shared by every layer position, so its faces come from the constructions using it as an
    outermost or innermost layer: in a SAM-written (forward) opaque construction the last layer
    is the zone face and the first the outer face; the unstamped reverse construction mirrors
    it; any other construction's exposed position supplies both faces. A single-layer use
    therefore keeps the edit on both faces (no differing faces are invented); an object in no
    exposed position keeps it on the external face and each stated internal one.
  - **Merging.** All usable face and thickness objects of one source Guid become one SAM
    material (its Guid restored from the object carrying `SAM.Guid`); construction layers keep
    their own thicknesses. They merge only when thermal properties, authored optics and, per
    face, each resolved value of the objects supplying it agree; otherwise a warning
    (SAM-OSI-ID-001) names the conflict and each object is imported on its own. Matching is by
    the explicit Guid, never by name. Each object is validated before a merged material is
    returned for it: only a validated member of the group resolves to it.
  - Malformed or inconsistent metadata is warned (SAM-OSI-ID-001 identity, SAM-OSI-MAT-001
    values) and the object is imported as its own material with its own thermal properties —
    never merged with, or aliased to, a valid group of the same source Guid — and its single
    native value on both faces. An object without the metadata (a third-party or older OSM)
    imports silently as before.
- **Import layer order (since SAM_OpenStudio#33 revision 11).** An opaque layered construction
  used by a planar surface is read innermost layer first (`ToSAM/Construction.cs`), so SAM's
  first layer is the one facing the surface's zone. An interzone panel is built from, and first
  related to, the side carrying the SAM-written forward construction (SAM type `Construction`
  with a SAM Guid), also after a geometric-fallback pairing; without that stamp, from the first
  surface visited. Spaces are added in an order that puts each forward side's space first, because the
  writer takes a panel's first space from the cluster's space order. Export → import → export therefore
  keeps every layer, thickness and used face. A panel whose building side is then second (a mixed or edited
  model) is re-read from its partner surface (information SAM-OSI-APX-001).
  - **Fail closed (error SAM-OSI-CON-002).** Every imported opaque surface panel is checked against its
    native surfaces. The checks:
    - its first space's surface holds the SAM layers innermost first and a second side their mirror;
    - each native StandardOpaqueMaterial layer, on both sides and in each side's direction, has the
      thickness, conductivity, density and specific heat of the SAM layer it became (merged source names
      alone would hide, for example, a 20 mm object on one side of a 12.5 mm wall);
    - the absorptances the writer would put on every face EnergyPlus uses equal the native ones.

    When one SAM panel cannot keep them, the import is unsuccessful (no model) instead of changing a layer
    or a used face. Massless and air-gap layers keep their nominal-thickness import and are matched by
    material only.
  Fenestration (glazing, gas, opaque door panes), air boundaries, non-layered constructions and
  constructions no planar surface uses keep OpenStudio's order. A construction also used by a
  subsurface or shading surface is split into an innermost-first SAM construction for the
  surfaces (SAM Guid restored) and one in OpenStudio's order for the other uses (own Guid;
  SAM-OSI-APX-001 information).
  - **Compatibility.** Every multi-layer opaque import now has the opposite SAM layer order to
    imports made before revision 11 (U-value unchanged; layer positions, thermal-mass position
    and exposed faces change, which is the correction). SAM models imported earlier keep their
    reversed constructions; nothing identifies them, and no automatic migration is made. Re-import
    the source OSM to correct such a model.

## TransparentMaterial → OS:WindowMaterial:Glazing (StandardGlazing)

EnergyPlus defines the glazing **Front side as "the side of the layer opposite the zone"**
(exterior-facing for exterior windows) and **Back side as the side closest to the zone**
(I/O Reference, *Materials for Glass Windows and Doors*). SAM `External*` is the
building-exterior-facing side and `Internal*` the room-facing side, therefore:

**External → Front, Internal → Back.**

Note: SAM_LadybugTools `EnergyWindowMaterialGlazing.cs` maps Internal → Front and External →
Back. Independent review (P1-04) found that assignment inverted relative to the EnergyPlus
definition above; this converter deliberately deviates from the LadybugTools reference here.
(Symmetric glass is unaffected either way — which is why the reference's tests never showed it.)

| SAM source | Unit | OpenStudio field | Missing-value policy |
| --- | --- | --- | --- |
| `ConstructionLayer.Thickness` | m | Thickness | as opaque rule |
| `ThermalConductivity` | W/m·K | Conductivity | **error** if invalid |
| `TransparentMaterialParameter.SolarTransmittance` | 0–1 | Solar Transmittance at Normal Incidence | warning; OS default |
| `TransparentMaterialParameter.ExternalSolarReflectance` | 0–1 | Front Side Solar Reflectance | warning; OS default |
| `TransparentMaterialParameter.InternalSolarReflectance` | 0–1 | Back Side Solar Reflectance | warning; OS default |
| `TransparentMaterialParameter.LightTransmittance` | 0–1 | Visible Transmittance at Normal Incidence | warning; OS default |
| `TransparentMaterialParameter.ExternalLightReflectance` | 0–1 | Front Side Visible Reflectance | warning; OS default |
| `TransparentMaterialParameter.InternalLightReflectance` | 0–1 | Back Side Visible Reflectance | warning; OS default |
| — | — | Infrared Transmittance | fixed 0 (LadybugTools parity) |
| `TransparentMaterialParameter.ExternalEmissivity` | 0–1 | Front Side IR Hemispherical Emissivity | warning; OS default 0.84 |
| `TransparentMaterialParameter.InternalEmissivity` | 0–1 | Back Side IR Hemispherical Emissivity | warning; OS default 0.84 |
| — | — | Dirt Correction Factor | fixed 1 |
| — | — | Solar Diffusing | fixed No |

## GasMaterial — usage-dependent (review P1-06)

A SAM `GasMaterial` has **two different OpenStudio targets**, selected by an explicit
construction-usage context (`OpenStudioMaterialUsage`), never inferred from the material name:

| Context | OpenStudio object | EnergyPlus object |
| --- | --- | --- |
| Layer of an opaque `Construction` (wall/floor/roof) | `OpenStudio.AirGap` | `OS:Material:AirGap` |
| Pane layer of an `ApertureConstruction` | `OpenStudio.Gas` | `OS:WindowMaterial:Gas` |

`OS:WindowMaterial:Gas` inside an opaque construction makes EnergyPlus fail in
`InitConductionTransferFunctions` ("R Value below lowest allowed value", R = 0.000) — the exact
fatal failure observed in the first Rhino 8 smoke-test model (AR90UP 50 mm cavities).

### Opaque cavity → OS:Material:AirGap

| SAM source | Unit | OpenStudio field | Conversion | Missing-value policy |
| --- | --- | --- | --- | --- |
| `GasMaterialParameter.HeatTransferCoefficient` h | W/m²K | Thermal Resistance R [m²K/W] | **R = 1 / h** | **error** SAM-OS-MAT-001 when missing, NaN, infinite or ≤ 0; **error** when R < 0.001 m²K/W (EnergyPlus per-layer minimum from `InitConductionTransferFunctions`) |

Authority for the property and units: SAM `Query.HeatTransferCoefficient` documents the value as
"Heat Transfer Coefficient (Thermal Conductance) [W/m2K]" (EDSL reference), and SAM's
`Query.UpdateHeatTransferCoefficients` computes it from gas type, thickness and tilt
(`AirspaceConvectiveHeatTransferCoefficient(tilt, thickness)` for opaque air cavities) and stores
it on the material via `GasMaterialParameter.HeatTransferCoefficient`. The companion SAM query
`Query.AirspaceThermalResistance` ("Thermal Resistance of airspace in Opaque Construction
according to BS EN ISO 6946:2017 [m2K/W]") confirms the opaque-cavity model is a resistive
layer — thickness alone is *not* a valid resistance model.

(Reference note: SAM_LadybugTools' legacy opaque path computes R via
`AirspaceThermalResistance(angle, thickness)`; its `New` path feeds the parameter to Honeybee's
`rValue` directly. Neither is copied: the parameter is a conductance, so R = 1/h.)

### Pane gas → OS:WindowMaterial:Gas (unchanged)

| SAM source | Unit | OpenStudio field | Policy |
| --- | --- | --- | --- |
| `Analytical.Query.DefaultGasType(gasMaterial)` (parameter `GasMaterialParameter.DefaultGasType`, else name matching) | enum | Gas Type | Air/Argon/Krypton/Xenon direct; any other value (incl. Undefined, SulfurHexaFluoride) → **error** SAM-OS-MAT-001 |
| `ConstructionLayer.Thickness` | m | Thickness | as opaque rule |

### Material-family validation

* Opaque constructions accept only `StandardOpaqueMaterial`, `MasslessOpaqueMaterial` and
  `AirGap` — an `OpenStudio.Gas` or a glazing material in an opaque layer set is rejected with
  SAM-OS-CON-001/SAM-OS-MAT-001 before any CLI execution.
* Fenestration pane constructions accept `StandardGlazing`, `Gas` and `StandardOpaqueMaterial`
  (opaque doors) — an `AirGap` in a pane set is rejected.
* A failed gas layer is never silently omitted: material conversion failure fails the whole
  construction with an error diagnostic.
* Material cache keys include the usage: `<Guid>:OpaqueAirGap:<R>` and `<Guid>:WindowGas:<thickness>`
  (other materials remain `<Guid>:<thickness>`). One SAM GasMaterial used in both contexts
  produces two distinct OpenStudio objects — never shared.

## Constructions

* `Construction.ConstructionLayers` → `OS:Construction` with resolved material layers,
  Forward/Reverse per the table above. Empty/missing layer list → **error** SAM-OS-CON-001.
  A layer whose material name is not found in `AnalyticalModel.MaterialLibrary` → **error**
  SAM-OS-CON-001 naming the material. No hidden default materials, ever.
* `ApertureConstruction.PaneConstructionLayers` → `OS:Construction` (`:Pane:Forward` /
  `:Pane:Reverse` cache keys).
* `ApertureConstruction.FrameConstructionLayers` → `OS:WindowProperty:FrameAndDivider` (C3,
  documented approximation): width from `ApertureConstructionParameter.DefaultFrameWidth` else
  the SAM frame thickness; conductance U = 1/Σ(thickness/conductivity) over the frame layers
  (no film coefficients); solar/visible absorptance = 1 − `External*Reflectance` of the
  outermost frame layer. When a frame is applied the SubSurface polygon becomes the SAM **pane**
  polygon (`Aperture.GetPaneFace3Ds()`), because EnergyPlus grows the frame outward from the
  glass; area conservation (pane strictly inside the aperture) is validated. Any invalid or
  incomplete frame data falls back to the full-polygon frameless conversion with a
  SAM-OS-CON-002 warning — never wrong geometry. Frames on opaque doors are skipped
  (information diagnostic).
* `PanelType.Air` → one shared `OS:Construction:AirBoundary` (no direction variants),
  assigned to both sides of the panel.
* OpenStudio rejecting a layer set (`setLayers` returns false, e.g. mixed opaque and
  fenestration layers) → **error** SAM-OS-CON-001.

## Deduplication

* Materials: cached per `(material Guid, thickness)`; gas materials additionally per usage
  (`<Guid>:OpaqueAirGap:<R>` / `<Guid>:WindowGas:<thickness>`). The SAM Guid is registered once
  in the object map (first variant).
* Constructions: cached per `(construction Guid, direction)`; Guid registered once
  (Forward variant).
* Deduplication is by Guid, never by name alone.
