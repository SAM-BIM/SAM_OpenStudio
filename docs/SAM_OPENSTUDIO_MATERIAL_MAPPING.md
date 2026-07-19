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

## GasMaterial → OS:WindowMaterial:Gas (Gas)

| SAM source | Unit | OpenStudio field | Policy |
| --- | --- | --- | --- |
| `Analytical.Query.DefaultGasType(gasMaterial)` (parameter `GasMaterialParameter.DefaultGasType`, else name matching) | enum | Gas Type | Air/Argon/Krypton/Xenon direct; any other value (incl. Undefined, SulfurHexaFluoride) → **error** SAM-OS-MAT-001 |
| `ConstructionLayer.Thickness` | m | Thickness | as opaque rule |

## Constructions

* `Construction.ConstructionLayers` → `OS:Construction` with resolved material layers,
  Forward/Reverse per the table above. Empty/missing layer list → **error** SAM-OS-CON-001.
  A layer whose material name is not found in `AnalyticalModel.MaterialLibrary` → **error**
  SAM-OS-CON-001 naming the material. No hidden default materials, ever.
* `ApertureConstruction.PaneConstructionLayers` → `OS:Construction` (`:Pane:Forward` /
  `:Pane:Reverse` cache keys). Frame layers are NOT converted in the MVP (documented gap).
* `PanelType.Air` → one shared `OS:Construction:AirBoundary` (no direction variants),
  assigned to both sides of the panel.
* OpenStudio rejecting a layer set (`setLayers` returns false, e.g. mixed opaque and
  fenestration layers) → **error** SAM-OS-CON-001.

## Deduplication

* Materials: cached per `(material Guid, thickness)`; the SAM Guid is registered once in the
  object map (first variant).
* Constructions: cached per `(construction Guid, direction)`; Guid registered once
  (Forward variant).
* Deduplication is by Guid, never by name alone.
