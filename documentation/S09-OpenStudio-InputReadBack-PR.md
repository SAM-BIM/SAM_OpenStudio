# S09: OpenStudio independent native-input read-back and ventilation alignment

Task S09 of the SAM simulation roadmap (SAM `documentation/SIMULATION_ROADMAP_Q4_2026.md`). The contract is SAM
`documentation/SIMULATION_INPUT_MAPPING_S07.md`. It follows S08, which completed in SAM_Tas#106 (merge `61eb0d37`,
closeout `c50e9169`; post-merge Build 39580480516 succeeded on `61eb0d37`).

Branch `feature/s09-openstudio-input-readback`, base `sow/2026-Q4` (`7f216657`). **Draft, for independent review. Not
merged.**
- **Tested content (revision 2):** `cfe097f470fe05ef4e5c7c372a70954d33b9ec13`.
- **Revision 1:** tested content `ebd9c4e4`, reviewed head `d6d1cc5b6573ddb3453355e7468ccc83d3ee2419`.
- The final head and its hosted checks are in the PR description.

## Status

- **Revision 2 closes the two gaps the independent review of `d6d1cc5b` reproduced.**
  - Native multiplicity (Zone, ZoneGroup and window multipliers) and building orientation (coordinate system, Building
    North Axis, zone relative north) are now checked against SAM intent.
  - A contradiction FAILs; an uninterpretable configuration is BLOCKED technical (see "Revision 2").
  - All six new negatives, and the reviewer's three copies, FAIL (exit 10). The reviewed head reported all nine as
    BLOCKED owner, exit 12.
- **Ventilation policy implemented and verified natively.**
  - V0/V1: no delivered outdoor air. The requirement is kept.
  - V2: hourly delivery = the SAM requirement × the selected profile.
  - A selection that does not resolve is an invalid input.
  - Changing V2 back to V1 is covered by tests.
- **Both fixtures: complete S07 coverage, BLOCKED (owner decision named), exit 12**, unchanged by revision 2:
  - `s08-fix-box`: 316/316 required comparisons.
  - `s08-fix-two`: 586/586 required comparisons.
  - Every other quantity matches within the S07 tolerance, or is a verified transformation or not applicable. Two named
    items remain; both are owner decisions, not defects (see "Fixture verdicts").
  - The box repeat gives the same verdict and a byte-identical IDF.
- **All 14 targeted negatives FAIL** (exit 10): the original 8 and the 6 new ones.
- **The optics decision table for OS-MAT-SINGLE-SIDED is below.** It separates exposed-face differences (18 box rows /
  33 two-zone rows) from buried-layer metadata (51 / 105). The item stays proposed and owner-unaccepted.
- **Construction-performance calculation methods are assessed below.** This is an assessment only; nothing is
  implemented.
- On this evidence the S09 roadmap criterion ("as S08, for OpenStudio") is met **subject to** the owner deciding the
  named items and to independent review. PASS needs either a writer change for the optics item or the owner's
  acceptance, which gives "Not PASS — accepted limitation", never PASS.

## Owner instructions applied

**S09 start (2026-10-21, in substance).**
- **Fixtures and weather:** the S08 signed-off fixtures, byte for byte, and the S08 Budapest weather.
- **Method:**
  - Do not regenerate fixtures, tune or invent tolerances.
  - Read OSM and IDF native fields in a fresh process, deriving the expectations independently from SAM and S07.
  - Corroborate identity with geometry.
  - Reuse the S08 COM-free logic.
- **Ventilation policy:**
  - V0/V1: no delivered outdoor air in either engine; the requirements are kept.
  - V2: a valid selected profile controls the hourly delivery.
  - A selected but missing or wrong-type profile is an invalid input.
  - No Tas profile is added to copy an OpenStudio default.
  - Correct OpenStudio's unscheduled delivery, covering the SpaceType DSOA, the per-space override, inheritance and the
    Ideal Loads controls.
- **Authorised:** native generation, translation, fresh-process reads and disposable negatives.
- **Not authorised:** Tas execution, simulation, sizing or recalibration.

**Revision 2 (2026-10-21, in substance).**
- Close the reproduced gaps: multiplicity and orientation inconsistent with the frozen fixtures must be detected,
  either by accounting for effective quantities or by explicit rejection, never assuming unity. Add fresh-process
  negatives for each case.
- Prepare the optics decision table for both fixtures. Keep OS-MAT-SINGLE-SIDED proposed and owner-unaccepted.
- Assess construction-performance calculation methods without implementing a calculator or changing the UI.
- Keep the fixtures, weather and tolerances.
- Excluded: a material-writer fix, fixture regeneration, tolerance changes, shared-core redesign, Tas execution,
  simulation, sizing, merge, issue closure and the broader issue #30.

## Work completed

| Commit | Change |
|---|---|
| `814dba1` | Writer: the delivery rule for the SpaceType DSOA and the per-space override; explicit Ideal Loads outdoor-air controls and DSOA link (see "Writer change") |
| `2099572` | `benchmark/SAM.Analytical.OpenStudio.InputReadBack` (reader, comparator, report) and `.Cli` (`os-input-readback` export / read / compare); fixtures copied with their S08 hashes |
| `7d6507c` | `negative` mode (8 cases), `Run-S09-ReadBack.ps1`, `Pack-S09-Evidence.ps1`, 33 tests, CI step and SAM_Tas sparse clone, `.sln`, mapping doc, D1 test narrowed |
| `ebd9c4e` | The OS-MAT-SINGLE-SIDED proposal named in each report; acceptance evidence |
| `d6d1cc5` | Revision 1 record |
| `cfe097f` | **Revision 2:** `OsComparison.Orientation` / `.Multiplicity`, the `Unsupported` (BLOCKED technical) list, report schema `/2`, reader classes, 6 negatives, 17 tests, acceptance script |
| (record commit) | Revision 2 evidence (acc2, A/B, optics table program and output) and this record |

### Writer change (`SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/`)

- **`InternalCondition.cs`:** `ToOpenStudio_VentilationDeliverySchedule` chooses the DSOA fraction schedule.
  - No selection: `SAM_Schedule_VentilationNotDelivered`, all 0, with information `SAM-OS-HVAC-001`, as the other Ideal
    Loads notes use.
  - A resolved Ventilation profile: that profile.
  - Otherwise: error `SAM-OS-SCH-001` and the all-zero schedule. It is never unscheduled.
  - The DSOA keeps every requirement rate.
- **`AnalyticalModel.cs`:** the `OutsideSupplyAirFlow` override states method Sum, its flow and every other rate as 0.
  Before, it relied on OpenStudio's Flow/Person defaults. It uses the same delivery schedule.
- **`IdealLoads.cs`:** DCV `None`, `NoEconomizer`, heat recovery `None`, and the space's effective DSOA (its own, else the
  inherited SpaceType one) linked explicitly.

Measured before the fix (local exploration): the two-zone V1 IDF delivered both requirements unscheduled. The S09
comparator reports this as EXPORT-DEFECT, i.e. FAIL. `neg-v1-unscheduled` reproduces it on the fixed export.

### Read-back design

1. **Export:** the production writer converts the frozen fixture with the explicit EPW and saves the OSM without running
   the CLI. The saved OSM is reloaded and translated with the SDK's `EnergyPlusForwardTranslator` (default options, as
   the CLI). `export.json` records the hashes, the SDK version and the diagnostics.
2. **Read (fresh process):** an IDD-free text parser of the OSM and the IDF.
   - Every field is checked against the file's own `!-` label, so an unknown layout is unreadable.
   - Schedules are expanded hour by hour: IDF `Schedule:Compact` ("For: AllDays", whole-hour `Until:`) and
     `Schedule:Constant`, and OSM `OS:Schedule:FixedInterval`. Anything else is unreadable.
   - It records the PID, the start time, the hashes before and after reading, and the hash of the EPW the OSM references.
3. **Compare (fresh process):**
   - Infrastructure checks as in S08: a different PID, the reader started after the export, the OSM and IDF read are the
     exported ones, and the frozen model and EPW are byte-identical. A failed check exits 70.
   - Expectations: S08 `ReadBackExpected.cs`, linked, plus the core formulas.
   - Gate: the S08 core `SAM.Analytical.InputEquivalence`, by project reference. Neither is copied, and revision 2
     changes neither.
   - No SAM `ToSAM`, writer cache or SDK call is used for a native value.
4. **Identity:** each SAM space's native space is the one whose surfaces match its panel polygons one to one
   (`MatchPolygons`).
   - Apertures match through the native outline: the pane grown by the native frame width.
   - Writer name carriers may only contradict a match. The fixture space GUIDs share their first 8 hex digits, so for
     spaces the geometry does the work.
   - Unmatched native spaces, surfaces and subsurfaces, and objects that add air, heat or geometry (ZoneVentilation,
     mixing, AirflowNetwork, other equipment, simple-geometry surfaces and subsurfaces, ...), FAIL.
5. **Placement (revision 2):** the native multiplicity and orientation must be the single, unrotated building SAM
   states (see "Revision 2").
6. **OSM and IDF consistency:** these must agree, or the fixture FAILs:
   - the effective DSOA (own, else inherited) against the DSOA the IDF Ideal Loads delivers;
   - the DSOA rates and schedule;
   - every compared schedule, hour by hour;
   - the weather location;
   - zone and subsurface multipliers, and the building north axis;
   - no dangling schedule references.

## Revision 2: multiplicity and orientation (independent review of `d6d1cc5b`)

The independent review reproduced three IDF mutations of the box run. Each left the report unchanged at 316/316,
BLOCKED owner, exit 12:
- `Zone` Multiplier 1 → 2;
- `FenestrationSurface:Detailed` Multiplier blank → 2 (both windows);
- `Building` North Axis blank → 90.

S07 has no multiplicity or orientation row. Revision 2 adds neither a row nor a tolerance, and does not touch the
shared core. It checks both against SAM intent and fails closed.

**SAM intent.**
- SAM has no zone, zone-list or aperture multiplier: each SAM space and aperture is one physical object, and every S07
  row compares that one object.
- SAM states the building rotation only as `AnalyticalModelParameter.NorthAngle` (radians, clockwise from North).
  Unstated, the writer leaves the OpenStudio/EnergyPlus default 0 (`OS/SimulationSettings.cs:33-47`). Neither fixture
  states it.

**EnergyPlus 25.1 semantics.** These are version-matched to the SDK `3.10.0+86d7e215a1` translator (IDF
`Version 25.1`; I/O Reference, Group Thermal Zone Description/Geometry):
- A Zone Multiplier multiplies the zone's floor area, loads and internal-gain energy (default 1).
- A ZoneGroup multiplies a zone list on top of that.
- A subsurface Multiplier multiplies the window area, frame and divider included (default 1).
- Relative coordinates rotate the vertices by the Building North Axis plus each Zone's Direction of Relative North,
  and move them by the zone origin. World coordinates use neither north axis.
- The local ladybug IDD is 25.2, not matched; it only corroborates that Coordinate System is required (Relative or
  World) and that the fenestration Multiplier defaults to 1, with non-integers truncated.

**Rules.** A blank field is read as its documented default, never assumed.

| Native finding | Outcome |
|---|---|
| An effective multiplier ≠ 1 (Zone, ZoneGroup, FenestrationSurface:Detailed) | Consistency → **FAIL**: contradicts SAM |
| Relative coordinates with Building North Axis + Zone relative north ≠ 0, where SAM states no rotation | Consistency → **FAIL** |
| The OSM and IDF disagree (OS:ThermalZone / OS:SubSurface Multiplier, OS:Building North Axis) | Consistency → **FAIL** |
| An unreadable or non-integer multiplier; an unreadable north axis; an unknown or missing coordinate system; World coordinates with a non-zero north axis; a non-identity OS:Space transformation; a non-zero SAM North Angle (S07 has no orientation tolerance) | `Unsupported` → **BLOCKED technical** (exit 11), never PASS |
| Simple-geometry `Window`, `Door`, `GlazedDoor` and their `:Interzone` forms (which carry multipliers) | read, and refused as native extras: **FAIL** |

The report schema is now `s09-readback-report/2` (it adds `Unsupported`). Each fixture report carries one Supporting
line for orientation ("the native building is unrotated, as SAM states") and one for multiplicity ("every effective
multiplier is 1, as SAM states").

**Before and after.** The same disposable inputs were read and compared by each binary in fresh processes
(`documentation/S09-OpenStudio-InputReadBack-evidence/revision-2/`):

| Input | Reviewed head `d6d1cc5b` | Revision 2 |
|---|---|---|
| s08-fix-box, unchanged | BLOCKED owner, exit 12, 316/316 | the same; outcome counts identical |
| s08-fix-two, unchanged | BLOCKED owner, exit 12, 586/586 | the same; outcome counts identical |
| reviewer's zone-multiplier copy | exit 12 | **FAIL, exit 10** |
| reviewer's window-multiplier copy | exit 12 | **FAIL, exit 10** |
| reviewer's building-north copy | exit 12 | **FAIL, exit 10** |
| `neg-zone-multiplier` / `-both` | exit 12 / 12 | **FAIL 10 / 10** |
| `neg-window-multiplier` / `-both` | exit 12 / 12 | **FAIL 10 / 10** |
| `neg-north-axis` / `-both` | exit 12 / 12 | **FAIL 10 / 10** |

- **The `-both` variants** also mutate the OSM, so the OSM and IDF agree and only the check against SAM intent refuses
  them. Their reports contain only `multiplicity: …SAM states one … and no multiplier` or `orientation: Zone '…' is
  rotated by 90 deg …; SAM states no rotation`.
- **The reviewer's copies** differ from the committed box IDF only in the mutated lines.

## Decisions and assumptions (for review)

1. **T-VENT-INACTIVE for OpenStudio.**
   - It applies the S08 owner decision 3 preconditions unchanged, under the S09 owner policy (V0/V1 deliver nothing):
     - no selection;
     - the DSOA is present with its requirement;
     - its fraction schedule is read in both the IDF and the OSM, hour-labelled and exactly 0 for all 8760 hours;
     - every delivered flow is exactly 0;
     - approved rules only.
   - It covers rows 6 (ventilation) and 8b only.
   - Without it, V1 would be the G1 UNLISTED-DEFAULT (BLOCKED owner). **Pending: the owner should confirm the
     extension.**
2. **8a per basis:** an unstated basis is S-ZERO once any basis is stated. The S07 8a requirement is the sum, and the core's
   `ExpectedRequiredSupplyFlow` adds nothing for it. In V0 it stays S-UNSPEC. The summed requirement is also compared
   (`ventilation.requirement.total`).
3. **OS outdoor-air precedence (S07 section 3):** the override replaces the condition bases. For an override, SAM's
   requirement is its flow and the other bases are S-ZERO. Tas ignores `OutsideSupplyAirFlow`, so a model with an
   override diverges between the engines. The fixtures have none.
4. **Single-sided opaque optics (OS-MAT-SINGLE-SIDED):**
   - EnergyPlus `Material` has one solar, visible and thermal absorptance for both faces, and the writer writes the
     external value (`SAM-OS-MAT-002`, `OS/Material.cs:92-116`).
   - Each internal-side row whose SAM value differs is listed MAPPING-DIFFERENCE by S09, from the writer's documented
     behaviour, not by S07. Rank 3.
   - **Proposed, owner-unaccepted.** The decision table and options are below.
5. **Lighting visible fraction:** the fixtures do not author `LightingViewCoefficient`; the writer writes the S07-listed
   default 0.25 (DOCUMENTED-DEFAULT, rank 3). **Pending owner acceptance.**
6. **Native floor area and volume** are computed from the IDF surface polygons, because the Zone fields are blank
   (autocalculated); a stated Zone value would FAIL. Opaque reflectance = 1 − absorptance.
7. **The OS weather row (9) and T-FRAME:**
   - Row 9 compares the referenced EPW by SHA-256 and the location fields with the S07 OpenStudio tolerances (1e-4 deg,
     0.5 m).
   - Time zone is exact, because S07 states no OpenStudio tolerance for it.
   - The T-FRAME frame conductance uses 1e-5 W/m²K.
   - These are defined in `OsQuantities.cs`. No new tolerance is introduced.
8. **Design days:** OS precedence (no DDY, none in the model) gives none; native none is NOT-APPLICABLE.
9. **Multiplicity and orientation (revision 2):**
   - **Rejected, not accounted for.** S07 compares one SAM object with one native object, and an effective-quantity
     scaling would need per-row semantics S07 does not define.
   - Contradictions use the existing fail-closed Consistency channel. The new `Unsupported` list floors the status at
     BLOCKED technical, never PASS.
   - Neither adds a quantity, a tolerance or a core change.

## Fixture verdicts (approved rules)

Evidence: `documentation/S09-OpenStudio-InputReadBack-evidence/`, revision-2 acceptance `acc2`. Harness
`os-input-readback.dll` `32090bbf…`; library `5f279f12…`.

| Run | Status (exit) | Coverage | Outcomes | Non-PASS (all owner decisions) |
|---|---|---|---|---|
| `s08-fix-box` (V2) | BLOCKED owner (12) | 316/316 | Match 220, WithinTolerance 25, NotApplicable 1, MappingDifference 69, DocumentedDefault 1 | OS-MAT-SINGLE-SIDED (69 rows), lighting visible fraction |
| `s08-fix-two` (V1, V1) | BLOCKED owner (12) | 586/586 | Match 393, WithinTolerance 46, AcceptedTransformation 6, NotApplicable 1, MappingDifference 138, DocumentedDefault 2 | OS-MAT-SINGLE-SIDED (138 rows), lighting visible fraction (2 zones) |
| `box-repeat` | BLOCKED owner (12) | 316/316 | identical report | IDF byte-identical |

- Every `report-approved.txt` is byte-identical to revision 1's. Only the JSON changed: schema `/2`, the orientation
  and multiplicity Supporting lines, and fresh hashes and times.
- **Ventilation:**
  - Box V2: 8b passes for all 8760 hours, and the 6.hourly ventilation schedule matches.
  - Two-zone: both zones are V1, with the requirement read back and matched (8a). 8b and the ventilation schedule pass as
    T-VENT-INACTIVE.
- **Hourly series:** all 11 per fixture zone (5, 6, 8b) cover 8760 hours with alignment verified from the native labels.
  The calendar is RunPeriod 2006 (non-leap), 1/1-12/31, with an 8760-row EPW.
- **Placement:** both fixtures unrotated (Relative coordinates, Building North Axis blank, no zone rotation, SAM North
  Angle unstated). All multipliers are 1: box 1 zone and 2 subsurfaces, two-zone 2 zones and 1 subsurface, no ZoneGroup.
- **Provisional rules:** the same exit codes (baseline).

**Negatives** (`neg-*`; each a disposable copy, read and compared fresh). All 14 FAIL (exit 10), and the originals are
unchanged:

| Case | Mutation | Result |
|---|---|---|
| identity-swap-names | two plain walls trade names | FAIL (identity mismatch) |
| schedule-swap | box DSOA schedule set to the occupancy schedule | FAIL (8b, 6, OSM/IDF) |
| v1-unscheduled | two-zone DSOA schedules removed in IDF and OSM (the old behaviour) | FAIL (8b EXPORT-DEFECT) |
| missing-dsoa-link | box Ideal Loads names no DSOA | FAIL (8b, inheritance) |
| missing-schedule-object | the DSOA names an undefined schedule | FAIL (dangling reference) |
| people-density | People density × 1.01 | FAIL (4a, 4b, 8a, 8b) |
| layer-conductivity | first Material conductivity × 1.001 | FAIL (3a, 3b) |
| osm-dsoa-schedule | OSM-only: the DSOA schedule removed | FAIL (OSM/IDF consistency) |
| zone-multiplier | IDF Zone Multiplier 1 → 2 (OSM unchanged) | FAIL (multiplicity vs SAM; OSM/IDF) |
| window-multiplier | IDF window Multipliers blank → 2 (OSM unchanged) | FAIL (multiplicity vs SAM; OSM/IDF) |
| north-axis | IDF Building North Axis blank → 90 (OSM unchanged) | FAIL (orientation vs SAM; OSM/IDF) |
| zone-multiplier-both | as zone-multiplier, plus OS:ThermalZone Multiplier → 2 | FAIL (multiplicity vs SAM only) |
| window-multiplier-both | as window-multiplier, plus OS:SubSurface Multipliers → 2 | FAIL (multiplicity vs SAM only) |
| north-axis-both | as north-axis, plus OS:Building North Axis → 90 | FAIL (orientation vs SAM only) |

## OS-MAT-SINGLE-SIDED: optics decision table (proposed, owner-unaccepted)

**Sources:**
- the comparator's own rows on the acc2 runs, printed by the evidence program
  `documentation/S09-OpenStudio-InputReadBack-evidence/revision-2/optics-table/` (output `optics-box.txt`,
  `optics-two.txt`);
- translator OpenStudio SDK `3.10.0+86d7e215a1`, IDF `Version 25.1`;
- writer `OS/Material.cs:92-116`: `StandardOpaqueMaterial` gets SAM's *external* emissivity and 1 − external
  reflectances.

**EnergyPlus 25.1 semantics:**
- `Material` has one Thermal, Solar and Visible Absorptance (I/O Reference, Material). A construction lists layers
  outside to inside (Construction).
- The Opaque Construction Report in `eplusout.eio` gives a construction's Outer/Inner Thermal and Solar Absorptance as
  those of its outermost/innermost layer (Output Details, eplusout.eio). So only the outermost layer's outside face and
  the innermost layer's zone face carry optics into the heat balance; buried faces carry none.
- An interzone partition's outside face is the other zone's inside face (mirrored construction), so each physical face
  is counted once, on the zone it faces.
- Ground and adiabatic outside faces receive no solar or sky long-wave exchange.

**Reading the table:**
- Layers are listed outside → inside as the IDF lists them for that zone side.
- "Needed" is the SAM face that is physically exposed. SAM lists a construction from its first space inside to outside,
  so on the first space's side the zone face is SAM's *internal* face; on the second space's side it is SAM's
  *external* face.
- Values are ρs / ρv / ε: native ρ = 1 − absorptance, native ε = Thermal Absorptance. Δ = native − needed.

| Fixture | Construction (zone sides) | Layers, outside → inside | Outer face | Zone face: SAM needed → native, Δ |
|---|---|---|---|---|
| box | FIX_EXT_WALL (4) | Render, Blockwork, MineralWool, Plasterboard | Render ext 0.57/0.58/0.88 = native | Plasterboard **int** 0.68/0.70/0.91 → 0.62/0.64/0.90, **Δ −0.06/−0.06/−0.01** |
| box | FIX_ROOF (1) | Membrane, OSB, PIR, Plasterboard | Membrane ext 0.14/0.15/0.92 = native | Plasterboard int, **Δ −0.06/−0.06/−0.01** |
| box | FIX_GRD_FLOOR (1) | Concrete, EPS, Screed | to ground: optics unused | Screed **int** 0.33/0.34/0.90 → 0.27/0.28/0.94, **Δ −0.06/−0.06/+0.04** |
| two | FIX_EXT_WALL (A 3, B 3) | as box | Render ext = native | Plasterboard int, **Δ −0.06/−0.06/−0.01** |
| two | FIX_ROOF (A, B) | as box | Membrane ext = native | Plasterboard int, **Δ −0.06/−0.06/−0.01** |
| two | FIX_GRD_FLOOR (A, B) | as box | to ground: unused | Screed int, **Δ −0.06/−0.06/+0.04** |
| two | FIX_PARTITION, A side | Timber, Stud, MineralWool, Plasterboard | interzone: B's zone face governs | Plasterboard int, **Δ −0.06/−0.06/−0.01** |
| two | FIX_PARTITION, B side | Plasterboard, MineralWool, Stud, Timber | interzone: A's zone face governs | Timber **ext** 0.41/0.40/0.86 = native, Δ 0 |

| Internal-side 3a rows listed MAPPING-DIFFERENCE | box | two |
|---|---|---|
| Total | 69 | 138 |
| At an exposed face where SAM needs the internal value (zone faces × 3 properties) | **18** (6 faces) | **33** (11 faces) |
| On buried faces, ground faces or faces where SAM needs the external value (no EnergyPlus effect) | **51** | **105** |

- **Exposed, physically different:** two material faces only, the Plasterboard and Screed zone faces. ρs and ρv are
  0.06 low; ε is 0.01 low (Plasterboard) and 0.04 high (Screed). Every outdoor-facing face matches, and so does the
  partition's B face.
- **Buried, metadata:** a layer's second set of optics on a face EnergyPlus never uses. S07 row 3a compares absorptance
  and emissivity layer by layer on both faces, so a single-valued native material gives three non-matching rows per
  layer whose SAM faces differ, *whichever* value the writer chooses. These rows are not a physical limitation; they
  come from the comparison scope. So not all 69 / 138 rows are physical limitations.
- **Representable by construction-aware material variants (writer change, not implemented; out of S09 scope):** every
  affected construction has 3 or more layers, and its zone-facing layer is a different material from its outside-facing
  layer.
  - A variant such as `FIX_Plasterboard` written with the internal values and used only where it is the innermost
    layer on the first space's side makes all 18 / 33 exposed-face rows match.
  - The partition is representable too: A's zone face (Plasterboard, needs internal) and B's (Timber, needs external)
    are different materials.
- **What variants do not change:** under the current 3a scope the non-matching rows of a variant layer move from its
  internal key to its external key, and the 51 / 105 buried rows stay. Removing them needs a contract decision (compare
  only the faces EnergyPlus uses), not a writer change.
- **Single-layer constructions stay constrained.** Neither fixture has one. With one layer both faces are exposed and
  one `Material` cannot carry two values. Splitting it into two half-thickness layers would change the layer structure
  3a compares, and the CTF discretisation. The variable-absorptance material property in the 25.1 I/O Reference is
  documented to modify only the outside face; it was not assessed.
- **Glazing is not affected:** `WindowMaterial:Glazing` carries front and back reflectance and emissivity, and all 3c
  rows match.
- **Owner options (nothing here accepts or authorises any):**
  1. accept OS-MAT-SINGLE-SIDED as a limitation for both fixtures (rank 5, never PASS);
  2. a separate SAM_OpenStudio writer task for construction-aware variants, which fixes the exposed faces;
     single-layer constructions would still need a rule;
  3. a separate S07/S09 contract decision on whether 3a gates optics on faces EnergyPlus does not use.

  Options 2 and 3 are independent.

## Construction-performance calculation methods (assessment only; nothing implemented)

**Scope.** Trace and compare only. No calculator, UI, backend, tolerance or owner decision is added. No Tas
calculation, EnergyPlus simulation or sizing was run.

**Sources.** Code at the checked-out heads (SAM `ee377167`, SAM_UI `45263ff0`, SAM_Tas `c50e9169`, all on
`sow/2026-Q4` with clean trees, and this branch), plus the EnergyPlus 25.1 documentation.

**Path aliases.**
- `ST/` = SAM_Tas `SAM_Tas/SAM.Analytical.Tas/`
- `UI/` = SAM_UI `WPF/SAM.Analytical.UI.WPF/`
- `SA/` = SAM `SAM/SAM.Analytical/`
- `IE/` = SAM_Tas `benchmark/SAM.Analytical.InputEquivalence/`

### What exists

1. **Tas-backed evaluators (SAM_UI)** are the source of every U, g and LT that SAM shows or stores.
   - **How they call Tas:** `ST/Classes/ThermalTransmittanceCalculator.cs` writes the SAM layers into a temporary TCD
     document (`ST/Modify/Run.cs`, `ST/Convert/ToTCD/Construction.cs`, `ST/Modify/UpdateMaterial.cs`). It then reads
     Tas `GetUValue()` and `GetGlazingValues()`.
   - **What comes back:** `GetUValue()` gives 7 values (external/internal × horizontal/up/down, plus transparent).
     `GetGlazingValues()` gives LT, light reflectance, direct solar τ/ρ/α, g and two shading coefficients.
   - Films, gap physics and heat-flow handling are Tas's, not SAM's. The Tas opaque film values are UNCONFIRMED.
   - **Consumers:** `UI/Classes/UValue/TasUValueEvaluator.cs`, `UI/Classes/Thermal/TasConstructionUValueEvaluator.cs`,
     `UI/Classes/Glazing/TasGlazingEvaluator.cs`, `UI/Classes/GlazingBuilder/DraftGlazingEvaluator.cs`.
   - **Uw:** `UI/Query/GlazingUw.cs` computes (Ug·Ap + Uf·Af)/(Ap + Af), or 80/20 without geometry, with no Ψ term.
2. **`SA/Classes/Glazing.cs`** is SAM's own normal-incidence calculator, in the EN 410 style.
   - Optics: multi-reflection layer optics, band-averaged in practice. The spectral path needs weights, and both callers
     pass none.
   - Thermal split: qi from a resistance network with he 23 and hi 8.
   - Outputs g, τv, ρ and per-pane absorptance. **It has no U output.**
   - Callers: the two Grasshopper components only.
3. **Gap coefficient helpers** produce Tas inputs, not results:
   - `SA/Query/AirspaceConvectiveHeatTransferCoefficient.cs` (ISO 6946);
   - `SA/Query/HeatTransferCoefficient.cs` and `SA/Query/NusseltNumber.cs` (EN 673 A(Gr·Pr)^n);
   - `SA/Query/UpdateHeatTransferCoefficients.cs`, whose coefficient Tas then uses as the gap conductance.
4. **The S08/S09 U recomputation** (`IE/Query/ExpectedThermal.cs`; `OsComparison.NativeU`):
   - opaque U = 1/(Rsi + Σ t/λ + Rse) with the S07 F-OPQ films;
   - glazing U = 1/(1/7.7 + Σ t/λ + 1/25) (F-GLZ-U).

   **It is one shared formula applied to the SAM layers and to the native layers. It shows input-conversion fidelity
   (the native thickness and λ reproduce SAM's). It is not an EnergyPlus calculation and validates no physics.**
   - It models no gap radiation or convection, no emissivity, no gas properties, no temperature dependence and no
     glazing heat-flow direction.
   - Gas layers are handled differently on the two sides: the SAM side would treat a `GasMaterial` as conduction only,
     while the native side returns Unreadable for `WindowMaterial:Gas` and `Material:AirGap`. Neither fixture has one.
5. **EnergyPlus 25.1** is the engine of the OpenStudio route.
   - **Opaque:** the `.eio` Opaque Construction Report gives Thermal Conductance **without** films. The simulation uses
     dynamic films, so no fixed-film U is part of it.
   - **Window heat balance:** a layer-by-layer nonlinear balance solved iteratively each timestep (Engineering Reference,
     Window Heat Balance Calculation), with:
     - glass layers as conductances with front/back emissivity;
     - absorbed solar per layer, split between its faces;
     - gap conduction and convection from the gas properties;
     - linearized gap radiation from the facing emissivities;
     - ISO 15099 room-side convection.
   - **Window reports:** the `.eio` Window Construction Report gives:
     - centre-of-glass Conductance with films at winter conditions (21 °C in, −18 °C out, 5.5 m/s);
     - SHGC at summer conditions (24 / 32 °C, 2.8 m/s, 783 W/m² normal beam);
     - normal-incidence solar and visible transmittance.
   - **Inputs:**
     - `WindowMaterial:Glazing` optics are SpectralAverage or Spectral;
     - `WindowMaterial:Gas` is Air, Argon, Krypton, Xenon or Custom;
     - `WindowMaterial:SimpleGlazingSystem` takes a rated U, SHGC and optional VT.

### Capability comparison

| Quantity | Tas-backed evaluators (SAM_UI) | `SAM.Analytical.Glazing` | S09 recomputation | EnergyPlus 25.1 (version-matched) |
|---|---|---|---|---|
| Opaque U | Tas `GetUValue`, 6 direction/side slots, Tas films | — | F-OPQ fixed films, Σ t/λ: conversion check only | no fixed-film U; `.eio` conductance without films |
| Centre-of-glass Ug | Tas `GetUValue()[6]`, pane construction | — (no U) | F-GLZ-U fixed films, solid layers only | `.eio` winter centre-of-glass conductance with films |
| Whole-window Uw | UI area-weighted Ug/Uf (80/20 fallback), no Ψ | — | — | frame via `WindowProperty:FrameAndDivider`; whole-window reporting not traced (UNVERIFIED) |
| g / SHGC | Tas `GetGlazingValues()[5]` | g = τe + Σ qi·αi | not computed (3c derived g informational) | `.eio` summer centre-of-glass SHGC |
| Solar / visible τ, ρ | Tas τe, ρe, LT, light ρ | yes, external and internal incidence | layer inputs compared only | normal-incidence τ in `.eio`; angular in the simulation |
| Per-layer absorption | no (total direct solar α only) | yes (external incidence) | — | yes, in the heat balance (not in `.eio`) |
| Emissivity | written to Tas; used inside Tas | only through an optional gap radiative term | compared as an input (3a/3c), not used | glazing front/back; opaque one value per material |
| Gas gaps | SAM HTC → Tas conductance; gas type not written | one caller-supplied conductance per gap | not handled (native Unreadable) | from gas type and properties, convection and radiation |
| Spectral vs averaged | band-averaged inputs | band-averaged (spectral path unused) | — | SpectralAverage or Spectral |

### Reusable code, validation gaps and recorded issues

- **Reusable:** the OS writer already maps pane layers to `StandardGlazing`/`Gas`, frames to `FrameAndDivider` and simple
  glazing to `SimpleGlazingSystem`. The S09 export already produces a translated IDF, and the S09 reader parses
  EnergyPlus text without the IDD.
- **Validation gap:** no independent reference test exists for any of these. There is no EN 410 / EN 673 / ISO 6946
  worked example, and no WINDOW or EnergyPlus comparison, in SAM, SAM_UI, SAM_Tas or SAM_OpenStudio. The SAM_UI tests
  use fakes; the InputEquivalence tests restate the formula.
- **Recorded issues (verified by reading the code; not reproduced, which would need Tas):**
  - **The reported Ug display problem.** `ThermalTransmittanceCalculator.cs:466-468` (and `:508-510`) guards
    `values.Length >= 6` and then reads `values[6]`. `ST/Modify/Run.cs` swallows every exception, and the UI shows
    Ug ≤ 0 as "Not calculated". This is recorded in SAM_UI `documentation/Thermal-StageE0-1.md:123`. Tas normally
    returns 7 values, so the guard misfires only on a short array; the swallowed exception is the wider risk.
  - **Swapped interpolation in `SA/Query/NusseltNumber.cs:54-81`.** The 0–45° branch interpolates from the 90° (wall)
    coefficients and the 45–90° branch reaches the 0° (horizontal) coefficients at 90°. The coefficient therefore jumps
    at the exact angles. Found by reading; not run.
  - **Pane-only Ug stored as the aperture U.** `ApertureParameter.ThermalTransmittance` stores the pane-only Ug, not Uw
    (SAM_UI `WPF/SAM.Analytical.UI.WPF/Modify/SetGlazing.cs:230-251`, from the `-pane` construction found at
    `ST/Classes/ThermalTransmittanceCalculator.cs:370-371`).
  - **Different gap physics in the two engines.** The gas type is not written to Tas, and the OS writer sends
    EnergyPlus only the gas type and thickness (`OS/Material.cs:266-302`, `ToOpenStudio_WindowGas`), dropping SAM's
    λ and HTC.

### Standalone EnergyPlus route (from source and documentation only)

- **Route:** reuse the S09 export (writer → OSM → SDK 3.10.0 translation) on a minimal model that holds the
  constructions under test. EnergyPlus 25.1 initialisation writes the `.eio` construction reports quoted above.
- **Dependencies:** an EnergyPlus 25.1 executable that matches the SDK. This workstation's CLI is ladybug
  OpenStudio 3.11 (IDD 25.2, not matched). The NuGet SDK has no EnergyPlus.
- **UNVERIFIED:**
  - whether the reports are written without a sizing or weather period (SimulationControl all No);
  - whether the tabular Envelope Summary is a usable alternative.

  Both need a bounded, separately authorised run.

### Smallest later scope (a recommendation only; not started)

1. **A read-only harness mode** that runs EnergyPlus 25.1 initialisation on a minimal IDF per construction set and parses
   the `.eio` Opaque and Window Construction Reports. No UI change and no backend adoption.
2. **Acceptance evidence**, each item reported per quantity with its boundary conditions stated, and no tolerance until
   the owner sets one:
   - the constructions of both S08 fixtures plus one gas-filled double glazing;
   - EnergyPlus conductance, SHGC and τ against the S09 shared-formula values (opaque conductance without films should
     agree to rounding);
   - the same against Tas `GetUValue`/`GetGlazingValues`;
   - against one independent published reference (an EN 673/EN 410 worked example or a WINDOW export).
3. **Separately**, the two code defects above, each in its own repository with a regression test.

## Files changed

- `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/InternalCondition.cs`, `AnalyticalModel.cs`,
  `IdealLoads.cs`
- `benchmark/SAM.Analytical.OpenStudio.InputReadBack/**` (new):
  - `Native/`, `Compare/`, `Report/`, `Classes/`;
  - `Fixtures/` (S08 bytes, `SHA256SUMS.txt`, README);
  - `Run-S09-ReadBack.ps1`, `Pack-S09-Evidence.ps1`.
- `benchmark/SAM.Analytical.OpenStudio.InputReadBack.Cli/**`, `benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/**`
  (new)
- `SAM_OpenStudio.sln`, `.gitattributes` (fixtures `-text`), `.github/workflows/build.yml`
- `docs/SAM_OPENSTUDIO_INTERNAL_CONDITION_MAPPING.md`, `tests/SAM.Analytical.OpenStudio.Tests/D1/DiagnosticDetailTableTests.cs`
- `documentation/S09-OpenStudio-InputReadBack-PR.md` (this record), `documentation/S09-OpenStudio-InputReadBack-evidence/**`
- Revision 2 changed:
  - `Compare/OsComparison.cs`, `Native/NativeReader.cs`, `Report/OsReport.cs`, `Run-S09-ReadBack.ps1`;
  - `Cli/Negative.cs`, `Tests/ReadBackTests.cs`;
  - the evidence `runs/`, `negatives/` and `summary-*.json` (repacked from acc2), and the new `revision-2/` folder.

  No writer, shared-core, fixture, weather or tolerance change.

## Tests and validation (local, Release x64)

| Check | At | Result |
|---|---|---|
| S09 tests (export in-process; read and compare in fresh processes) | `cfe097f4` | **50/50**: the 33 revision-1 tests unchanged, plus 17 new: 9 negatives (6 box, 3 two-zone), 3 SAM-intent-only, 4 BLOCKED-technical, 1 fixture placement |
| The same 50 tests with `Orientation()` and `Multiplicity()` disabled (the reviewed head's behaviour) | working tree, then restored | **17 failed, 33 passed**: exactly the new tests fail |
| `tests/SAM.Analytical.OpenStudio.Tests` (writer regressions) | revision-2 working tree (no writer or test change) | 388 passed, 2 skipped (390 = the `[Test]`/`[TestCase]` count at the base `7f216657` and here; revision 1 recorded 363/2, which is superseded) |
| `benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests` (not Simulation) | same | 51/51 |
| Acceptance and negatives, `C:\TasOut\s09\acc2` | revision-2 harness | 3 runs as in "Fixture verdicts"; 14/14 negatives FAIL; originals unchanged |
| Before/after on the same inputs, `C:\TasOut\s09\rev2-ab` | `d6d1cc5b` vs revision 2 | see "Revision 2" |
| Revision 1 mutations M1 (V1 delivers fraction 1), M2 (override unscheduled) | `ebd9c4e4` | 5 and 3 tests fail (not repeated) |

```text
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --filter TestCategory!=Simulation
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance -Root <new dir> -WeatherEpw <budapest.epw>
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance-negative -Root <same dir> -WeatherEpw <budapest.epw>
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Pack-S09-Evidence.ps1 -Root <same dir>
```

The before/after and optics commands are in `documentation/S09-OpenStudio-InputReadBack-evidence/revision-2/README.md`.

- **Prerequisites:**
  - sibling checkouts `SAM` (built into `SAM/build`) and `SAM_Tas` at `sow/2026-Q4`;
  - the Budapest EPW, decompressed from SAM `SAM/SAM/SAM.Tests/Resources/Weather/HUN_BU_Budapest.Met.Center.128400_TMYx.epw.gz`
    (sha256 `472ffb4e…`).
- **Machine-local** (not needed elsewhere): `C:\TasOut\s09\acc1` (revision 1), `C:\TasOut\s09\acc2` and
  `C:\TasOut\s09\rev2-ab` (revision 2).
- Building the reference writes ignored `bin/obj` output into the SAM_Tas core folder. The SAM_Tas working tree stays
  clean.
- **Hosted CI:**
  - The Build workflow clones SAM_Tas sparsely: the core and `ReadBack/`, branch candidates as for the other
    dependencies, which resolve to `sow/2026-Q4`. It logs the SHA, builds, and runs the S09 tests.
  - Final head and check results: see the PR description.

## Unresolved, risks, limits

- **Owner decisions (pending; none taken here):**
  - OS-MAT-SINGLE-SIDED: accept, or a separate writer task, and/or a 3a scope decision (see the decision table);
  - the lighting visible-fraction default;
  - the T-VENT-INACTIVE extension to OpenStudio.
- **Translator version:** the IDF comes from the OpenStudio SDK 3.10.0 translator (it matches the SDK that wrote the
  OSM). A production run translates with the CLI. This workstation's CLI is ladybug 3.11.0, and S06 prefers an
  SDK-matching CLI. The CLI translation was not exercised.
- **Not exercised by the fixtures:** internal floors and ceilings, internal apertures, gas glazing, `Material:AirGap`,
  shading, design days (T-DD), leap years, weekday sub-profiles, multi-space zones, an override in a real model.
  - Rotated models (a stated SAM North Angle) and World coordinates are refused as unsupported, not compared.
  - Single-layer opaque constructions are absent, so the single-layer optics constraint is untested.
  - The reader reports each of these as unreadable, unsupported or not applicable, never as a match.
- **S08 record discrepancy (record only; SAM_Tas not changed):** SAM_Tas
  `Documentation/S08-PRC-FixturesAcceptance-PR.md:78` lists the two-zone openings as "A framed south; B framed north,
  unframed east". The signed-off `s08-fix-two.json` (`20da9b58…`) contains one aperture (A, framed south), as its
  OSM/IDF do. The fixture bytes are unchanged; the S08 record text needs a separate correction.
- **Space-type reuse:** OpenStudio space types are shared per content hash, so the V1 note and the invalid-selection
  error appear once per SpaceType and once per override.

## Next step

Independent review of revision 2: GPT-6 or Claude Opus 5.5, effort High, a new session in read-only review mode, so the
reviewer is independent of this session.
- The owner decides the pending items above. An optics writer fix or a 3a scope change would each be a separate task.
- Then the owner's merge decision, the known-SHA `PROJECT_PROGRESS.md` closeout and guarded branch cleanup.
- The roadmap S09 row changes only through a separate SAM PR.
- The construction-performance route needs its own owner start and authorisation for any EnergyPlus run.
