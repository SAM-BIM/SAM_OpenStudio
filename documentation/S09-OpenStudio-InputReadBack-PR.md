# S09: OpenStudio independent native-input read-back and ventilation alignment

Task S09 of the SAM simulation roadmap (SAM `documentation/SIMULATION_ROADMAP_Q4_2026.md`). The contract is SAM
`documentation/SIMULATION_INPUT_MAPPING_S07.md`. It follows S08, which completed in SAM_Tas#106 (merge `61eb0d37`,
closeout `c50e9169`; post-merge Build 39580480516 succeeded on `61eb0d37`).

Branch `feature/s09-openstudio-input-readback`, base `sow/2026-Q4` (`7f216657`). **Draft, for independent review. Not
merged.** Tested content SHA: `ebd9c4e4d9ef101ccca696c90585dca3cd38af94`. The final head and its hosted checks are in
the PR description.

## Status

- **Ventilation policy implemented and verified natively.**
  - V0/V1: no delivered outdoor air. The requirement is kept.
  - V2: hourly delivery = the SAM requirement × the selected profile.
  - A selection that does not resolve is an invalid input.
  - Changing V2 back to V1 is covered by tests.
- **Both fixtures: complete S07 coverage, BLOCKED (owner decision named), exit 12.**
  - `s08-fix-box`: 316/316 required comparisons.
  - `s08-fix-two`: 586/586 required comparisons.
  - Every other quantity matches within the S07 tolerance, or is a verified transformation or not applicable. Two named
    items remain; both are owner decisions, not defects (see "Fixture verdicts").
  - The box repeat gives the same verdict and a byte-identical IDF.
- **All 8 targeted negatives FAIL** (exit 10). The originals are unchanged.
- On this evidence the S09 roadmap criterion ("as S08, for OpenStudio") is met **subject to** the owner deciding the
  two named items and to independent review. PASS needs either a writer change for the optics item or the owner's
  acceptance, which gives "Not PASS — accepted limitation", never PASS.

## Owner instructions applied (S09 start, 2026-10-21, in substance)

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

## Work completed

| Commit | Change |
|---|---|
| `814dba1` | Writer: the delivery rule for the SpaceType DSOA and the per-space override; explicit Ideal Loads outdoor-air controls and DSOA link (see "Writer change") |
| `2099572` | `benchmark/SAM.Analytical.OpenStudio.InputReadBack` (reader, comparator, report) and `.Cli` (`os-input-readback` export / read / compare); fixtures copied with their S08 hashes |
| `7d6507c` | `negative` mode (8 cases), `Run-S09-ReadBack.ps1`, `Pack-S09-Evidence.ps1`, 33 tests, CI step and SAM_Tas sparse clone, `.sln`, mapping doc, D1 test narrowed |
| `ebd9c4e` | The OS-MAT-SINGLE-SIDED proposal named in each report; acceptance evidence |

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
   - Gate: the S08 core `SAM.Analytical.InputEquivalence`, by project reference. Neither is copied.
   - No SAM `ToSAM`, writer cache or SDK call is used for a native value.
4. **Identity:** each SAM space's native space is the one whose surfaces match its panel polygons one to one
   (`MatchPolygons`).
   - Apertures match through the native outline: the pane grown by the native frame width.
   - Writer name carriers may only contradict a match. The fixture space GUIDs share their first 8 hex digits, so for
     spaces the geometry does the work.
   - Unmatched native spaces, surfaces and subsurfaces, and objects that add air or heat (ZoneVentilation, mixing,
     AirflowNetwork, other equipment, ...), FAIL.
5. **OSM and IDF consistency:** these must agree, or the fixture FAILs:
   - the effective DSOA (own, else inherited) against the DSOA the IDF Ideal Loads delivers;
   - the DSOA rates and schedule;
   - every compared schedule, hour by hour;
   - the weather location;
   - no dangling schedule references.

## Decisions and assumptions (for review)

1. **T-VENT-INACTIVE for OpenStudio.**
   - It applies the S08 owner decision 3 preconditions unchanged, under the S09 owner policy (V0/V1 deliver nothing):
     - no selection;
     - the DSOA is present with its requirement;
     - its fraction schedule is read in both the IDF and the OSM, hour-labelled and exactly 0 for all 8760 hours;
     - every delivered flow is exactly 0;
     - approved rules only.
   - It covers rows 6 (ventilation) and 8b only.
   - Without it, V1 would be the G1 UNLISTED-DEFAULT (BLOCKED owner). The owner should confirm the extension.
2. **8a per basis:** an unstated basis is S-ZERO once any basis is stated. The S07 8a requirement is the sum, and the core's
   `ExpectedRequiredSupplyFlow` adds nothing for it. In V0 it stays S-UNSPEC. The summed requirement is also compared
   (`ventilation.requirement.total`).
3. **OS outdoor-air precedence (S07 section 3):** the override replaces the condition bases. For an override, SAM's
   requirement is its flow and the other bases are S-ZERO. Tas ignores `OutsideSupplyAirFlow`, so a model with an
   override diverges between the engines. The fixtures have none.
4. **Single-sided opaque optics (OS-MAT-SINGLE-SIDED):** EnergyPlus `Material` has one solar, visible and thermal
   absorptance for both faces, and the writer writes the external value (`SAM-OS-MAT-002`). Each internal-side row whose
   SAM value differs is listed as MAPPING-DIFFERENCE by S09, from the writer's documented behaviour, not by S07. Rank 3:
   the owner either accepts it or requires a material-writer change (out of S09 scope).
5. **Lighting visible fraction:** the fixtures do not author `LightingViewCoefficient`; the writer writes the S07-listed
   default 0.25 (DOCUMENTED-DEFAULT, rank 3 until accepted).
6. **Native floor area and volume** are computed from the IDF surface polygons, because the Zone fields are blank
   (autocalculated); a stated Zone value would FAIL. Opaque reflectance = 1 − absorptance.
7. **The OS weather row (9) and T-FRAME:**
   - Row 9 compares the referenced EPW by SHA-256 and the location fields with the S07 OpenStudio tolerances (1e-4 deg,
     0.5 m).
   - Time zone is exact, because S07 states no OpenStudio tolerance for it.
   - The T-FRAME frame conductance uses 1e-5 W/m²K.
   - These are defined in `OsQuantities.cs`. No new tolerance is introduced.
8. **Design days:** OS precedence (no DDY, none in the model) gives none; native none is NOT-APPLICABLE.

## Fixture verdicts (approved rules; `documentation/S09-OpenStudio-InputReadBack-evidence/`)

| Run | Status (exit) | Coverage | Outcomes | Non-PASS (all owner decisions) |
|---|---|---|---|---|
| `s08-fix-box` (V2) | BLOCKED owner (12) | 316/316 | Match 220, WithinTolerance 25, NotApplicable 1, MappingDifference 69, DocumentedDefault 1 | OS-MAT-SINGLE-SIDED (69 rows), lighting visible fraction |
| `s08-fix-two` (V1, V1) | BLOCKED owner (12) | 586/586 | Match 393, WithinTolerance 46, AcceptedTransformation 6, NotApplicable 1, MappingDifference 138, DocumentedDefault 2 | OS-MAT-SINGLE-SIDED (138 rows), lighting visible fraction (2 zones) |
| `box-repeat` | BLOCKED owner (12) | 316/316 | identical report | IDF byte-identical; OSM differs only in object handles and the run path |

- **Ventilation:**
  - Box V2: 8b passes for all 8760 hours, and the 6.hourly ventilation schedule matches.
  - Two-zone: both zones are V1, with the requirement read back and matched (8a). 8b and the ventilation schedule pass as
    T-VENT-INACTIVE.
- **Hourly series:** all 11 per fixture zone (5, 6, 8b) cover 8760 hours with alignment verified from the native labels.
  The calendar is RunPeriod 2006 (non-leap), 1/1-12/31, with an 8760-row EPW.
- **Provisional rules:** the same exit codes (baseline).

**Negatives** (`neg-*`; each a disposable copy, read and compared fresh):

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

## Tests and validation (local, Release x64, at `ebd9c4e4`)

| Check | Result |
|---|---|
| New S09 tests (offline; export in-process, read and compare in fresh processes) | 33/33 |
| `tests/SAM.Analytical.OpenStudio.Tests` | 363 passed, 2 skipped (baseline at `7f216657`: 363/2) |
| `benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests` (not Simulation) | 51/51 |
| Mutation M1: V1 delivers fraction 1 | 5 tests fail |
| Mutation M2: override unscheduled | 3 tests fail |
| Acceptance and negatives | see above; harness `os-input-readback.dll` `3ae640b4…` |

```text
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance -Root <new dir> -WeatherEpw <budapest.epw>
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance-negative -Root <same dir> -WeatherEpw <budapest.epw>
```

- **Prerequisites:**
  - sibling checkouts `SAM` (built into `SAM/build`) and `SAM_Tas` at `sow/2026-Q4`;
  - the Budapest EPW, decompressed from SAM `SAM/SAM/SAM.Tests/Resources/Weather/HUN_BU_Budapest.Met.Center.128400_TMYx.epw.gz`
    (sha256 `472ffb4e…`).
- Machine-local, not needed elsewhere: the raw runs in `C:\TasOut\s09\acc1`.
- Building the reference writes ignored `bin/obj` output into the SAM_Tas core folder. The SAM_Tas working tree stays
  clean.

**Hosted CI:**
- The Build workflow clones SAM_Tas sparsely: the core and `ReadBack/`, branch candidates as for the other dependencies,
  which resolve to `sow/2026-Q4`. It logs the SHA, builds, and runs the S09 tests.
- Final head and check results: see the PR description.

## Unresolved, risks, limits

- **Owner decisions:** OS-MAT-SINGLE-SIDED (accept, or a separate material-writer task); the lighting visible-fraction
  default; the T-VENT-INACTIVE extension to OpenStudio.
- **Translator version:** the IDF comes from the OpenStudio SDK 3.10.0 translator (it matches the SDK that wrote the
  OSM). A production run translates with the CLI. This workstation's CLI is ladybug 3.11.0, and S06 prefers an
  SDK-matching CLI. The CLI translation was not exercised.
- **Not exercised by the fixtures:** internal floors and ceilings, internal apertures, gas glazing, `Material:AirGap`,
  shading, design days (T-DD), leap years, weekday sub-profiles, multi-space zones, rotated zones, and an override in a
  real model.
  The reader reports each of these as unreadable or not applicable, never as a match.
- **Space-type reuse:** OpenStudio space types are shared per content hash, so the V1 note and the invalid-selection
  error appear once per SpaceType and once per override.

## Next step

Independent review of this draft (GPT-6 or Claude Opus 5.5, effort High, new session, read-only review mode). The owner
decides the three items above. If the optics item is to be fixed, that is a separate SAM_OpenStudio task. Then the
owner's merge decision, the known-SHA `PROJECT_PROGRESS.md` closeout and guarded branch cleanup. The roadmap S09 row
changes only through a separate SAM PR.
