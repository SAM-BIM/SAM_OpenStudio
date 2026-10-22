# S09: OpenStudio independent native-input read-back and ventilation alignment

Task S09 of the SAM simulation roadmap (SAM `documentation/SIMULATION_ROADMAP_Q4_2026.md`). The contract is SAM
`documentation/SIMULATION_INPUT_MAPPING_S07.md`. It follows S08, which completed in SAM_Tas#106 (merge `61eb0d37`,
closeout `c50e9169`; post-merge Build 39580480516 succeeded on `61eb0d37`).

Branch `feature/s09-openstudio-input-readback`, base `sow/2026-Q4` (`7f216657`). **Draft, for independent review. Not
merged.**
- **Tested content (revision 9, the review of revision 8: native edits, merging, inherited faces):** `354e5322fa3a4c31a984de911c48534e768c40e4`.
- **Revision 8 (the opaque face-optics correction):** tested content `fc83a6df1f843f82a043353616a2b4e42a94e6d6`, reviewed head
  `901e592ada5728db9f413f6cd96665f5210cf5a7` (Build 39755063548 and SPDX 39755063557 succeeded).
- **Revision 7 (the owner decisions and acceptance reporting; its optics acceptance is removed by revision 8):** tested
  content `614a7a8d70b6a70c5628a0ef3b57eaec0c5ec794`, head `7fccdccb2420bfcc4ad79761d1eee7f547c54d57` (Build
  39744024838 and SPDX 39744024866 succeeded).
- **Revision 6 (the lighting read-back guards):** tested content `225881f00971c7cab3596e94867924a11866e147`, reviewed head
  `8fd0ffc2e7a4d0c1e8463305d74f476e198e4ea7` (Build 39726520323 succeeded). It passed independent technical review
  (owner statement, 2026-10-22).
- **Revision 5 (the S07 §5.1 lighting split):** tested content `20f8fb65`, reviewed head
  `31af6022095b8367cf333da0d790dc258955acb0` (Build 39672548198 succeeded).
- **Revision 4:** tested content `0c232875c8b872e393cd1ddb883e61f63eed30a6`; record head `6a36afb0` (Build 39622605892
  succeeded).
- **Revision 3:** tested content `c1e53328`, reviewed head `e0a6ea1950455e04dbf086d164500fd8829867ec` (its hosted
  Build failed; see "Revision 4").
- **Revision 2:** tested content `cfe097f4`, reviewed head `6f7e6b7dcce37a8db82c997bc718e0b17d5e28e0`.
- **Revision 1:** tested content `ebd9c4e4`, reviewed head `d6d1cc5b6573ddb3453355e7468ccc83d3ee2419`.
- The final head and its hosted checks are in the PR description.

## Status

- **Revision 9 addresses the independent review of `901e592a`** (see "Revision 9"). Not yet independently reviewed.
  - Import keeps a native optical edit made after export, on the face its object carries, with a warning; an unchanged
    OpenStudio default stays unstated. The review's reproduction (light reflectance unstated, visible absorptance
    0.7 → 0.42) now re-exports 0.42; revision 8 lost the edit.
  - SAM-written face and thickness objects of one explicit source Guid merge into one SAM material, its Guid restored
    and each layer's thickness kept; conflicts are reported and not merged; never merged by name.
  - An unstated (absent or NaN) internal value on a zone face that takes the external value is reported at Summary
    (new SAM-OS-MAT-004); its provenance stays unspecified on import and in S09.
  - Fixture reports are byte-identical to revision 8: BLOCKED owner, exit 12. Tests: S09 97/97, writer/importer 409
    passed / 2 skipped, benchmark 51/51 (Simulation excluded). A/B: revision 8 fails the 9 + 2 new regressions.
  - Found, not fixed (pre-existing, outside the review): the importer keeps OpenStudio's outside-first layer order, so a
    multi-layer construction comes back reversed on round trip.
- **Revision 8 corrects the opaque-material mismatch instead of accepting it** (owner, 2026-10-22; see "Revision 8").
  - Writer: SAM's first layer carries its internal-face optics (an `_Internal` variant where its faces differ), every
    other layer its external ones, so every face EnergyPlus uses holds the authored value. Forward and reverse
    constructions stay exact mirrors.
  - A single-layer outdoor or interzone construction whose faces differ is a named error, SAM-OS-MAT-003, with no
    construction and an unsuccessful conversion. Ground and adiabatic single layers use the zone face.
  - The forward construction follows the panel's first space; a missing first side is a named error, never a swap.
  - Import recovers both authored face sets from explicit per-object metadata and diagnoses inconsistent metadata.
  - S09 derives the used faces itself and compares them exactly; a swapped zone face FAILs.
  - **Revision 7's optics acceptance is removed.** Both fixtures are **BLOCKED owner, exit 12**, complete (319/319,
    592/592). Every remaining non-PASS row (69 / 138) is on a face EnergyPlus does not use.
  - Tests: S09 95/95, writer/importer 400 passed / 2 skipped (Simulation excluded), benchmark 51/51. Not yet independently
    reviewed.
- **Still recorded from 2026-10-22:** ventilation A (T-VENT-INACTIVE extended, Decision 1) and lighting B1 (the
  Decision 5 reading; **OD-L1 stays open**, the Tas side needs separate approval). The optics option-1 acceptance is
  superseded by the owner's instruction to investigate and correct.
- **Revision 7 reported the optics acceptance (superseded by revision 8).** Both fixtures are now **NOT PASS, accepted limitation (rank 5), exit 14**,
  with complete coverage (319/319, 592/592) and unchanged outcome counts. They are never PASS.
  - The acceptance applies only under the approved rules, to a fixture whose id and frozen-model SHA-256 are both a
    reviewed fixture's, and only to the 3a MAPPING-DIFFERENCE rows. Any other model, fixture id or rule set stays
    BLOCKED owner, exit 12.
  - Every failure check is kept: a wrong opaque optical value still FAILs (exit 10) under the acceptance, and all
    existing negatives still FAIL.
  - S09 93/93 (5 new). Its final-head CI succeeded; it was not independently reviewed before revision 8 replaced it.
- **Not a Phase B PASS.** The roadmap Phase B gate needs S08-S11 PASS. Exit 12 is not PASS, and neither would an
  accepted limitation be; no roadmap exception is requested or made here.
- Revision 6 (below) passed independent technical review. It had left S09 BLOCKED on the optics and T-VENT-INACTIVE
  decisions; the lighting visible-fraction item was withdrawn by S07 amendment L1 (SAM#217), which revision 5 implements.
- **Revision 6 closes the two gaps the independent review of `31af6022` reproduced in the lighting read-back** (see
  "Revision 6"). The writer and importer are unchanged.
  - Native domain and energy conservation are checked before any S07 tolerance. An invalid split (a fraction outside
    [0, 1], or a sum above 1) is a named EXPORT-DEFECT on all five lighting rows: FAIL, exit 10. The reviewed head
    accepted p = 1 with a sum of 1.0008, and p = 0 with Fraction Radiant −0.0004 (exit 12).
  - The plenum-calculated return-air setting is read. Blank or No stays the static split. Yes, an uninterpretable value
    or an unreadable field blocks technically (exit 11), named, with no lighting row accepted.
  - S09 88/88 (8 new: 5 regressions, which fail against the reviewed comparator, and 3 valid controls). The fixture
    reports are byte-identical to revision 5.
- **Revision 5 implements S07 §5.1 (amendment L1, SAM#217 `113873a0`)** (see "Revision 5").
  - Writer: Fraction Radiant = p·(1 − s), Fraction Visible = p·s, s = 0.2415 / 0.576, Return Air Fraction unset (0);
    the view coefficient is never read. An authored p, including 0, is preserved. An absent or NaN p takes p₀ = 0.576.
    Both cases are stated at Summary (new `SAM-OS-IC-002`).
  - Importer: p = Fraction Radiant + Fraction Visible; the view coefficient is not set.
  - S09: five T-LIGHT-SPLIT rows per space from the raw SAM p and the §5.1 constants. The view-coefficient comparison
    and its DOCUMENTED-DEFAULT exception are removed. An unstated p stays S-UNSPEC (DOCUMENTED-DEFAULT, never PASS).
  - Both fixtures: complete coverage (319/319, 592/592), BLOCKED owner, exit 12, on the optics rows only. 80/80 S09
    tests, 5 new negatives FAIL on both fixtures.
  - Not run under this scope: the acceptance scripts, so the committed `acc3` pack predates L1.
- The independent review of `59036783` accepted the revision-4 code technically. The review of `31af6022` (revision 5)
  found the two gaps revision 6 closes. Revision 6 then passed independent technical review (owner statement,
  2026-10-22).
- **Record correction after that review (documentation only):** the writer-test reproduction command now excludes
  `TestCategory=Simulation`, and the historical 388 passed / 2 skipped results are separated from the reviewer's filtered
  363 passed / 2 skipped (see "Writer-test scope correction").
- **Revision 4 responds to the failed final-head Build 39603021836 at `e0a6ea19`** (see "Revision 4").
  - One negative's `compare` wrote no report, and the test lost its exit code and stderr.
  - A defect that produces exactly this failure is fixed: process freshness was judged by process id alone, and Windows
    reuses ids. A process is now its id and OS creation time.
  - A failed CLI step now reports its exit code, stdout/stderr and process identities before any report is parsed. CI
    keeps that text when a step fails.
  - That id reuse *was* the CI cause is likely but unproven, because the CI run kept no output.
  - Verdicts, coverage, outcome counts and every `report.txt` are unchanged (acc6 = acc3).
- **Revision 3 closes the finding the independent review of `6f7e6b7d` reproduced** (see "Revision 3").
  - `OsComparison.Multiplicity` skipped the OSM/IDF multiplier check when the IDF zone or subsurface had no
    `OS:ThermalZone` / `OS:SubSurface` of its name. Such a model kept BLOCKED owner, exit 12, with complete coverage.
  - Consistency is now claimed only against one identified OSM counterpart. A proven missing counterpart FAILs
    (exit 10); an ambiguous or unreadable identity, or an unknown Multiplier layout, is BLOCKED technical (exit 11).
  - The new negatives FAIL on both fixtures; the reviewed head reported every one as BLOCKED owner, exit 12.
  - Both unchanged fixtures keep complete coverage, their outcome counts and BLOCKED owner, exit 12.
  - The assessment wording is corrected (see "Construction-performance calculation methods").
- **Revision 2 closed the two gaps the independent review of `d6d1cc5b` reproduced.**
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
- **Revisions 1–4 (before L1; revision 5 above supersedes the counts): both fixtures: complete S07 coverage, BLOCKED
  (owner decision named), exit 12**, unchanged by revisions 2 and 3:
  - `s08-fix-box`: 316/316 required comparisons.
  - `s08-fix-two`: 586/586 required comparisons.
  - Every other quantity matches within the S07 tolerance, or is a verified transformation or not applicable. Two named
    items remain; both are owner decisions, not defects (see "Fixture verdicts").
  - The box repeat gives the same verdict and a byte-identical IDF.
- **All 16 targeted negatives FAIL** (exit 10): the original 8, the 6 of revision 2 and the 2 of revision 3. Revision 5
  adds 5 lighting negatives, which FAIL on both fixtures in the S09 tests.
- **The optics decision table for OS-MAT-SINGLE-SIDED is below.** It separates exposed-face differences (18 box rows /
  33 two-zone rows) from buried-layer metadata (51 / 105). Revision 7 recorded option 1 (acceptance); revision 8 replaces
  it with option 2 (construction-aware variants), so every exposed face now matches.
- **Construction-performance calculation methods are assessed below.** This is an assessment only; nothing is
  implemented. Revision 3 corrects its wording only.
- On this evidence every S07 quantity matches except the rows on faces EnergyPlus does not use (69 / 138), which keep
  both fixtures BLOCKED owner, exit 12, subject to the independent review of revision 8. Clearing them needs an owner
  decision: accept those unused-face rows, or an S07 3a scope decision (compare only used faces).

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

**Revision 3 (2026-10-21, in substance).**
- Fix one finding: the OSM/IDF consistency checks in `OsComparison.Multiplicity` are skipped when the matching
  `OS:ThermalZone` or `OS:SubSurface` is absent.
- Claim consistency only against an unambiguous OSM counterpart. A proven missing counterpart is a consistency failure;
  an unreadable identity or layout blocks technically. A missing object is never read as a present one with a default
  field. Keep legitimate blank-field defaults.
- Add fresh-process regressions on both fixtures; keep the fix local (no reader redesign) and every existing test.
- Correct the assessment wording: scope the Tas-backed U/g/LT claims to the traced WPF workflows; keep the short-array
  guard defect apart from the unproven cause of the Ug display issue; describe World coordinates as the code handles
  them; update the stale hosted-CI wording.
- Excluded: material-writer changes, calculator/UI work, shared-core changes, fixture, weather or tolerance changes,
  Tas calculations, EnergyPlus execution, simulation, sizing, installation, owner decisions, merge and next-stage work.

**Revision 4 (2026-10-21, in substance).**
- Diagnose the final-head Build 39603021836 failure without widening S09 scope.
- A failure must expose the CLI exit code, stdout/stderr and run evidence before the report is parsed. Keep bounded CI
  diagnostics on failure if needed.
- Reproduce under relevant conditions, investigate from evidence, and do not call it flaky because local tests pass.
- Make only evidence-supported fixes. Keep fresh-process independence, hash checks, fixture bytes, tolerances and every
  negative. No retries or relaxed assertions.
- Excluded as in revision 3.

**Revision 5 (2026-10-21, in substance).**
- Source of truth: SAM S07 §5.1, merged by SAM#217 as `113873a0`. Check its post-merge Test first.
- Preserve the authored total radiant proportion p, including 0. s = 0.2415 / 0.576 at full precision; visible p·s,
  long-wave p·(1 − s), convective 1 − p, return air 0. Never use the view coefficient as an energy fraction.
- Importer: p from radiant + visible, without the comfort coefficient, with the §5.1 diagnostics.
- Update mapping and coverage documents, the S09 read-back rows, independent expectations and targeted negatives. Test
  an authored 0, independence from the comfort coefficient, incorrect partitions and a nonzero return air.
- A missing or NaN p keeps its unspecified provenance; DOCUMENTED-DEFAULT never becomes PASS.
- OD-L1 stays open: do not settle fallback applicability or claim suitability for excluded cases. If OD-L1 blocks a
  production fallback change, continue the authored-input corrections and report the decision. Do not keep the old
  mapping and present it as corrected.
- Run S09, non-Simulation benchmark and writer tests; no acceptance scripts that export or execute engines.
- Excluded: opaque optics, reversed glazing, Ladybug mapping, duplicate SAM defaults, SAM_Tas edits, broad refactoring,
  simulation, sizing, Tas/EnergyPlus execution, S10, merge. Keep the PR draft.

**Revision 6 (2026-10-22, in substance).** Correct only two reproduced fail-closed gaps; keep the writer and importer
split.
- In `OsComparison.LightingSplit`, validate native fraction domains and energy conservation before applying the S07
  tolerances. Regressions: p = 1 with radiant (1 − s) + 0.0004 and visible s + 0.0004; p = 0 with radiant −0.0004 and
  visible 0.0004. Neither may produce accepted lighting rows without a named failure. Keep valid p = 0 and p = 1, document
  the floating-point boundary, and do not loosen S07 tolerances.
- Read the Lights plenum-calculated return-air setting. Its blank/No default stays supported. Yes cannot receive a
  verified static T-LIGHT-SPLIT: report a named unsupported, BLOCKED technical result. Uninterpretable settings fail
  closed. Add CLI/report regressions, including a valid unchanged control.
- Show that the new regressions expose the reviewed behaviour. Keep fixtures, original evidence and the OD-L1, optics
  and ventilation decisions open. No other repository edits, simulations, sizing, engine execution or merge.

**Revision 9 (2026-10-22, in substance).** Address the independent review of `901e592a`:
1. Preserve native optical edits when the SAM property was absent (reproduction: both Plasterboard light reflectances
   removed, export, native visible absorptance 0.7 → 0.42, import and re-export returned 0.7). Distinguish unchanged
   defaults from edits; keep edits with diagnostics or reject them explicitly.
2. Merge compatible face variants by explicit source identity into one SAM material, construction references restored
   and layer thicknesses kept. Diagnose conflicts; never merge by name.
3. Report internal-to-external inheritance at Summary; keep unspecified provenance; test absent and NaN.
- Add focused regressions and unchanged controls; run S09, writer/importer and benchmark (Simulation excluded); update
  evidence; final-head CI; return for independent review.
- Keep the corrected exposed faces, the single-layer refusal, lighting and zero-delivery ventilation. Excluded: engines,
  acceptance scripts, Tas changes, a renewed optics acceptance, merge, S10.

**Revision 8 (2026-10-22, in substance).** Approved in this PR with conditions.
- Preserve revision 7 in history; do not merge or close S09. Investigate, then correct, the opaque-material mismatch
  before accepting it.
- Single-layer outdoor or interzone constructions needing different face optics: a named error and an unsuccessful
  conversion, never External values as a substitute. Ground and adiabatic cases use the zone-face properties. Check per
  construction use, before cache reuse. No averaging or artificial layer splitting.
- Preserve enough explicit source information to recover both authored face sets on import, even when only an Internal
  variant is referenced. Do not merge by name or invent missing values. Diagnose inconsistent identity or properties.
- S09 derives the expected exposed-face properties from raw SAM data and orientation, without the writer's selector,
  with a swapped-face regression.
- Implement the orientation checks, remove revision 7's optics acceptance and keep its history.
- Run S09, writer/importer and benchmark tests, excluding `TestCategory=Simulation` from the writer and benchmark
  suites. Obtain final-head CI and return for independent review.
- Excluded: engines, acceptance scripts, Tas or roadmap changes, merge, S10. Keep zero-delivery ventilation and the
  reviewed lighting corrections; no glazing work.

**Owner decisions and revision 7 (2026-10-22, in substance).**
- Revision 6 at `8fd0ffc2` passed independent technical review.
- The owner approved the three recommendations of the advisory note: optics option 1, ventilation A, lighting B1.
- Accept optics only for the two reviewed fixtures, with result effects unmeasured.
- Authorised: the bounded #33 record and acceptance-reporting update. Preserve all failure checks and the pending OD-L1
  limits. Return for independent review after final-head CI.
- Excluded: merge, engines, Tas changes, a roadmap exception and S10.

## Work completed

| Commit | Change |
|---|---|
| `814dba1` | Writer: the delivery rule for the SpaceType DSOA and the per-space override; explicit Ideal Loads outdoor-air controls and DSOA link (see "Writer change") |
| `2099572` | `benchmark/SAM.Analytical.OpenStudio.InputReadBack` (reader, comparator, report) and `.Cli` (`os-input-readback` export / read / compare); fixtures copied with their S08 hashes |
| `7d6507c` | `negative` mode (8 cases), `Run-S09-ReadBack.ps1`, `Pack-S09-Evidence.ps1`, 33 tests, CI step and SAM_Tas sparse clone, `.sln`, mapping doc, D1 test narrowed |
| `ebd9c4e` | The OS-MAT-SINGLE-SIDED proposal named in each report; acceptance evidence |
| `d6d1cc5` | Revision 1 record |
| `cfe097f` | **Revision 2:** `OsComparison.Orientation` / `.Multiplicity`, the `Unsupported` (BLOCKED technical) list, report schema `/2`, reader classes, 6 negatives, 17 tests, acceptance script |
| `6f7e6b7` | Revision 2 evidence (acc2, A/B, optics table program and output) and this record |
| `c1e5332` | **Revision 3:** `OsComparison.OsmCounterpart`, `OsmMultiplier` read by position and label, the counterpart counts in the multiplicity Supporting line; 2 CLI negatives (and the acceptance script); 8 tests |
| `e0a6ea1` | Revision 3 evidence (acc3, A/B), the assessment wording corrections and this record |
| `0c23287` | **Revision 4:** `ProcessIdentity` (id + creation time) in the export/reader/compare checks, schemas `s09-export/2` and `s09-native/2`; `TestSupport` failure evidence and `s09-diagnostics`; 4 tests; CI diagnostics upload on failure |
| `5903678`, `6a36afb` | Revision 4 evidence (`revision-4/`: CI excerpt, A/B, id-reuse measurement), this record and its writer-test scope correction |
| `20f8fb6` | **Revision 5:** the S07 §5.1 lighting split in the writer (`LightingSplit`, `SAM-OS-IC-002`) and the importer; the S09 T-LIGHT-SPLIT rows; 5 negatives; 18 S09 and 16 writer/importer tests; manifests and mapping documents |
| `31af602` | Revision 5 evidence (`revision-5/`) and this record |
| `225881f` | **Revision 6:** the native-domain and plenum guards in `OsComparison.LightingSplit`, `OsQuantities.LightingFractionBoundary`, 6 CLI cases, 8 S09 tests |
| `8fd0ffc` | Revision 6 evidence (`revision-6/`) and this record |
| `614a7a8` | **Revision 7:** `Compare/OwnerDecisions.cs` (new), the acceptance in `OsComparison.Run` and its Supporting line, the model hash passed by `compare`; 5 S09 tests |
| `7fccdcc` | Revision 7 evidence (`revision-7/`) and this record |
| `fc83a6d` | **Revision 8:** face-specific opaque variants, the single-layer error SAM-OS-MAT-003, the forward-side check, face metadata and its import; S09 used-face comparison; revision 7's `OwnerDecisions` removed; writer/importer and S09 tests; docs |
| `901e592` | Revision 8 evidence (`revision-8/`) and this record |
| `354e532` | **Revision 9:** written-value stamps and SAM-OS-MAT-004 in the writer; edit detection and merging by source Guid in the importer; NaN as unstated in S09; 11 writer/importer and 2 S09 tests; docs |
| (record commit) | Revision 9 evidence (`revision-9/`) and this record |

## Revision 9: native edits, merging by source identity, inherited faces (review of `901e592a`)

Tested content `354e5322fa3a4c31a984de911c48534e768c40e4`.

**1. Native edits** (writer `Material.StampOpaqueFaces`; importer `MaterialLibrary.ReadFaceObject`, `ApplyEdits`):
- Every opaque object now also records what was written: `SAM.Written.ThermalAbsorptance`, `.SolarAbsorptance`,
  `.VisibleAbsorptance` (including inherited values and OpenStudio defaults).
- On import each absorptance is compared with its written value (or, for revision-8 objects without the record, with
  the authored face it came from). Unchanged: exactly the authored values, so an unchanged default stays unstated.
  Changed: an edit, kept as the SAM value of the face the object carries (internal for an `_Internal` object, external
  otherwise), with warning SAM-OSI-MAT-001 naming what was written and what is there now. The other face keeps its
  authored value. No record at all: the value is kept as an edit of unknown provenance, reported the same way.
- Inconsistent metadata (a written value that disagrees with its authored source) is warned and the single native
  value is imported on both faces, as in revision 8.
- Revision 8 treated a changed stated value as untrusted metadata (single native value on both faces) and ignored a
  changed unstated one; the reproduction lost the edit (A/B below).

**2. Merging by source identity** (`MaterialLibrary.MergedOpaqueMaterial`, `OpenStudioImportContext.OpaqueMaterialGroups`):
- All usable objects carrying the same `SAM.MaterialGuid` (face variants and thickness variants) become one SAM material.
  Its Guid is restored from the object that carries `SAM.Guid`; its name is that object's; construction layers reference
  it at their own OpenStudio thickness.
- They merge only when thermal properties (conductivity, density, specific heat) agree, the authored face optics agree,
  and, per face, every object resolves each optic to the same value (its edit, else the authored value). Otherwise
  warning SAM-OSI-ID-001 names the conflict and each object is imported on its own (with its own edits).
- An object without `SAM.MaterialFace`/`SAM.MaterialGuid` is never merged, whatever its name.

**3. Inherited zone-face values** (writer `Material.InheritedInternalOptics`; new code SAM-OS-MAT-004):
- When SAM's first layer lacks an Internal* value (absent or NaN) whose External* is stated, the external value is
  written on the zone face (as before) and **information SAM-OS-MAT-004** is raised once per material, Summary detail
  (`GetDiagnosticDetail`, pinned in `DiagnosticDetailTableTests` and `DiagnosticsAdapterTests`).
- The inherited value is not stamped as authored, so import keeps the internal value unspecified.
- S09 now reads a NaN SAM layer value as unstated (S-UNSPEC), like an absent one. S07 lists no default for an internal
  face, so a native value where SAM states none is an EXPORT-DEFECT (FAIL) on exactly those zone-face rows: the
  inheritance is declared by the writer, not accepted by S07. The S09 fixtures state every internal value: no MAT-004
  and byte-identical reports.

**Tests** (writer/importer `M4/OpaqueFaceOpticsTests.cs` 30, was 21; S09 2 new):

| Test | Required |
|---|---|
| `ANativeEditOfAnUnstatedValue_IsKeptAndReported_ThroughImportAndReexport` (review reproduction) | internal light reflectance 0.58 imported, external unstated, one MAT-001 warning "written 0.7, now 0.42"; re-export zone face 0.42 |
| `AnUnchangedDefault_StaysUnstated_ThroughImportAndReexport` (control) | no warning; unstated; re-export 0.7 |
| `AnEditOfAStatedValue_IsKeptOnItsFace_AndReported` | internal 0.55, external authored 0.5 |
| `FaceAndThicknessObjectsOfOneSource_MergeIntoOneSamMaterial_KeepingLayerThicknesses` | one material, source Guid, both faces; layers at 0.0125 and 0.015 |
| conflicts: thermal / authored optics / edits on one face | one SAM-OSI-ID-001 naming the conflict; not merged |
| `AnObjectWithoutSourceIdentity_IsNeverMergedByName` (control) | two materials; the unstamped one keeps its single value |
| `AnUnstatedInternalValue_IsReportedAtSummary_AndKeepsItsUnspecifiedProvenance` (absent, NaN) | one MAT-004 at Summary; external value written; not stamped; imported unstated |
| `StatedInternalValues_RaiseNoInheritanceNote` (control), `EveryOpaqueObject_RecordsWhatWasWritten` | no MAT-004; written stamps equal the object |
| S09 `AnUnstatedZoneFaceValue_KeepsItsUnspecifiedProvenance` (absent, NaN) | MAT-004 in the writer diagnostics; exactly the 5 Plasterboard zone-face light rows S-UNSPEC EXPORT-DEFECT; Screed zone face passes |

The two re-export tests read the single-layer ground floor (one layer, the zone face in either order) because of the
round-trip limit below.

**A/B** (`revision-9/test-summaries.txt`): with `Material.cs`, `MaterialLibrary.cs`, `OpenStudioImportContext.cs` and
`OsComparison.cs` of `901e592` restored, 9 of the 30 face-optics tests and both S09 provenance cases fail. The
reproduction fails exactly as reviewed (internal light reflectance unstated after import). The controls pass under both.

**Found, not fixed (pre-existing; outside the review's three items):** `ToSAM/Construction.cs` keeps OpenStudio's
outside-first layer order and says this survives a round trip, but the writer reverses SAM's inside → outside order, so
an imported multi-layer construction is reversed and a re-export puts its outermost layer on the zone face. Fixing it
changes import orientation for every model and needs its own decision and task.


Tested content `fc83a6df1f843f82a043353616a2b4e42a94e6d6`. Revision 7's commits and evidence stay in history; its acceptance code is removed.

**Verified findings (code and the revision-7 IDF; no engine run):**
- SAM stores layers inside → outside. The writer gave every opaque layer the External values, and one material object
  per (material, thickness) served every construction and position.
- The forward construction goes to the first space's side of an internal panel (and every single-sided surface), the
  reverse to the second; in the IDF they share material objects in mirror order. OpenStudio translation adds nothing.
- The writer and S09 both take "first space" from `AdjacencyCluster.GetSpaces()` order (S07 section 1).
- EnergyPlus uses only the outermost layer's outside face and the innermost layer's zone face; an interzone outside face
  is the other zone's inside face, and ground/adiabatic outside faces carry no optics (I/O Reference, as cited in the
  optics table; not run).
- Before revision 8, only one opaque object per SAM Guid carried `SAM.Guid`, and import wrote the single value on both
  faces.

**Writer** (`Material.cs`, `Construction.cs`, `AnalyticalModel.cs`; codes and keys in `SAM.Core.OpenStudio`):
- SAM's first opaque-construction layer is built with `internalFace`: when the material's Internal* optics differ, an
  internal-face variant (cache key `Guid:thickness:Internal`, name `…_Internal`) carries them; an unstated Internal*
  keeps the External* value. Every other layer is the external-face object. The face follows SAM layer position, so
  forward and reverse constructions share objects.
- Single layer (checked in pass D for each panel use before any construction is converted or reused): differing faces
  on any boundary other than ground/adiabatic, or on a two-sided panel, raise **error SAM-OS-MAT-003** naming the
  construction, boundary and properties; no construction is assigned, so `IsValid` is false. No substitute is written.
- Orientation: `ForwardSideProblem` refuses a panel whose first converted surface is not its first space's side (error
  SAM-OS-ADJ-001, no construction, never swapped).
- Every opaque object from a SAM opaque material carries `SAM.MaterialGuid`, `SAM.MaterialFace` and each stated
  `SAM.Optic.<parameter>` in `OS:AdditionalProperties` (no EnergyPlus input changes).
- The SAM-OS-MAT-002 "external values govern both sides" line now appears only for opaque door panes.

**Importer** (`ToSAM/MaterialLibrary.cs`): with face metadata, exactly the stamped authored values are set on both
faces (unstated ones stay unset). The metadata is trusted only if well formed, consistent with any `SAM.Guid` on the
object, matching the object's own absorptances for its declared face, and identical across face objects of the same
source Guid; otherwise a warning (SAM-OSI-ID-001 identity, SAM-OSI-MAT-001 values) and the single native value on both
faces. No metadata: unchanged behaviour, silent.

**S09** (`OsComparison.CompareLayers`, `IsExposedOpaqueFace`, `SingleLayerFaces`): the used faces are derived from SAM
and the side alone (innermost layer: SAM internal on the first space's side, SAM external on the other; outermost layer
of an outdoor side: SAM external). A used face is never listed, so a wrong value FAILs. Any other face is listed
MAPPING-DIFFERENCE only when SAM's two faces differ **and** the native value is the layer's other authored face. A
single-layer outdoor or interzone side with differing faces adds a named `Unsupported` entry (BLOCKED technical).
`OwnerDecisions` and the model-hash wiring are removed; `Run` and `compare` are as at revision 6.

**Fixture results (S09 test runs at `fc83a6d`; `revision-8/`):**

| Fixture | Status (exit) | Coverage | Outcomes |
|---|---|---|---|
| `s08-fix-box` | BLOCKED owner (12) | 319/319 | Match 213, WithinTolerance 32, AcceptedTransformation 4, NotApplicable 1, MappingDifference 69 |
| `s08-fix-two` | BLOCKED owner (12) | 592/592 | Match 379, WithinTolerance 60, AcceptedTransformation 14, NotApplicable 1, MappingDifference 138 |

- The MAPPING-DIFFERENCE count is unchanged (69 / 138) but every row is now on a face EnergyPlus does not use: the
  18 / 33 exposed rows pass (WithinTolerance where `1 − (1 − ρ)` is not bit-exact), and the zone-face variants' buried
  external rows are listed instead.
- The IDFs differ from revision 7 only in the `_Internal` material names and absorptances and the construction layers
  that name them (`revision-8/idf-diff-rev7.txt`).

**Tests** (writer/importer `M4/OpaqueFaceOpticsTests.cs`, 21; S09 9 new/changed):

| Test | Required |
|---|---|
| zone face / interzone mirror / first-space order | `_Internal` innermost on every single-sided surface; B side = A side reversed with B's zone face external; space B first → B forward |
| missing first side (unit) | `ForwardSideProblem` names the first space and "never swapped"; null when the first side is present |
| reuse / agreeing faces / metadata | two thermally identical Plasterboard objects; no variant when faces agree; every object stamped |
| single layer | ground and adiabatic use internal values; outdoor (4 walls) and interzone raise SAM-OS-MAT-003, invalid, no construction; per use in both conversion orders; agreeing faces written |
| import | both faces from the variant alone (Guid restored); base and variant; unstated value not invented; edited object, disagreeing objects and mismatched identity each warned; no metadata unchanged |
| S09 `EveryUsedZoneFace_HoldsTheAuthoredFace` | box 18 / two-zone 36 zone-face rows pass, unlisted, none non-PASS |
| S09 `ASwappedZoneFace_Fails_OnExactlyTheZoneFaceRows` | external values on each variant (the old writer): FAIL, exactly 18 / 33 internal zone-face rows |
| S09 `AWrongOpaqueOpticalValue_Fails`, single-layer ground / outdoor roof | FAIL; ground exit 12 with only listed rows; roof SAM-OS-MAT-003, named Unsupported, FAIL |

**A/B** (8 optics tests; `revision-8/test-summaries.txt`):
- Writer files of `7fccdcc` restored: all 8 fail. The fixtures FAIL (exit 10) with the zone faces OUT-OF-TOLERANCE, no
  SAM-OS-MAT-003 is raised, and there is no `_Internal` material to swap. The revision-8 comparator rejects the old
  writer on its own.
- Comparator of `8fd0ffc` (revision 6) restored with the revision-8 writer: all 8 fail. It lists used zone faces as
  MAPPING-DIFFERENCE, a swapped zone face stays BLOCKED owner (exit 12, hidden), and no single-layer entry is named.

**Untested or unverified:** EnergyPlus acceptance of the variants and of the missing construction left by
SAM-OS-MAT-003 (no engine run); a realistic model that drops only the first space's side (covered at unit level only);
massless and door-pane opaque layers (unchanged path); the effect on results (nothing simulated).

## Revision 7: the owner decisions and acceptance reporting

Tested content `614a7a8d70b6a70c5628a0ef3b57eaec0c5ec794`. Revision 6 (`8fd0ffc2`) passed independent technical review; the owner then took the three
decisions of the advisory note on 2026-10-22.

**Decisions recorded:**

| Item | Decision | Bounds |
|---|---|---|
| Optics (OS-MAT-SINGLE-SIDED) | Option 1: accepted as a limitation | The two reviewed fixtures only (`s08-fix-box` `45524172…`, `s08-fix-two` `20da9b58…`). Exposed zone faces differ by ρs/ρv −0.06 and ε −0.01 (Plasterboard) / +0.04 (Screed); 18 / 33 rows, the other 51 / 105 on faces EnergyPlus does not use. **Effect on results unmeasured** (nothing simulated). Never PASS. |
| Ventilation (T-VENT-INACTIVE) | A: the extension to OpenStudio confirmed (Decision 1) | Rows 6 and 8b, under the unchanged S08 owner decision 3 preconditions. Exercised by the two-zone V1/V1 fixture only; no V0 case or per-space override among the fixtures. |
| Lighting | B1: the Decision 5 reading confirmed | OpenStudio applies p₀ to an unstated p with `SAM-OS-IC-002`, claiming nothing outside suspended LED office lighting. **OD-L1 stays open.** No fixture uses the fallback; an unstated p stays DOCUMENTED-DEFAULT, never PASS. The Tas side needs a separate, OD-L1-gated SAM_Tas approval. |

Ventilation A and lighting B1 confirm behaviour revisions 1–6 already implement; neither changes code or a verdict.

**Acceptance reporting (`Compare/OwnerDecisions.cs`, `OsComparison.Run`, `Cli/ReadBackRead.cs`):**
- The S08 core already supports owner acceptances (`GateConfiguration.Acceptances`, `OwnerAcceptance` per quantity and
  per fixture, with a required decision reference; rank 5). S09 previously passed none, so every report was exit 12.
- `OwnerDecisions.For` returns one acceptance, (`3a`, fixture id, MAPPING-DIFFERENCE, the decision reference), only when
  the rule set is Approved **and** the fixture id and the frozen-model SHA-256 are a reviewed fixture's. Otherwise none.
- `compare` passes the model hash from the manifest, which its infrastructure check has already verified against the
  run's frozen model, so the acceptance is bound to those bytes. `OsComparison.Run` takes it as an optional argument;
  without it no acceptance applies.
- Only rows S09 lists as MAPPING-DIFFERENCE are affected: the internal-side opaque optical rows whose SAM value differs
  from the external one. A DOCUMENTED-DEFAULT (an unstated p, OD-L1), every FAIL and every BLOCKED result are unchanged.
  A single-valued material's external-face rows still compare the same native value with SAM, so a wrong optical value
  fails there.
- The OS-MAT-SINGLE-SIDED Supporting line names the acceptance and its reference when it applies, and is unchanged
  otherwise. No report schema, exit code, quantity, tolerance, writer, importer or core change.

**Fixture results (S09 test runs at `614a7a8`; `revision-7/`):**

| Fixture | Status (exit) | Coverage | Outcomes |
|---|---|---|---|
| `s08-fix-box` | NOT PASS, accepted limitation (14) | 319/319 | as revision 5; the 69 MappingDifference rows rank 5 |
| `s08-fix-two` | NOT PASS, accepted limitation (14) | 592/592 | as revision 5; the 138 MappingDifference rows rank 5 |

Against `revision-5/`, each `report.txt` differs only in its status line and in the "accepted by the owner (reference);
this is not a pass" reasons of the listed rows.

**New S09 tests (5):**

| Test | Required |
|---|---|
| `BothFixtures_HaveCompleteCoverage_AndOnlyTheNamedOwnerDecisionRemains` (changed) | both fixtures exit 14, no owner decision named, every non-PASS row rank 5 with the reference; 69 / 138 rows |
| `TheOpticsAcceptance_NeedsTheReviewedFixtureIdAndApprovedRules` (3) | the unchanged box run compared as `s08-fix-two`, as an unknown id, or under the provisional rules: exit 12, the optics decision named |
| `TheOpticsAcceptance_DoesNotApplyToAChangedModel` | the box re-saved with the same p (other bytes): exit 12, the same outcome counts |
| `AWrongOpaqueOpticalValue_StillFails_UnderTheAcceptance` | Plasterboard Solar Absorptance 0.5 in the IDF: FAIL (exit 10) on 3a external-face rows only; the internal-face rows keep rank 5 |

Tests that change the model (lighting p, comfort coefficient, ventilation) keep exit 12: their bytes are not reviewed.

**A/B:** with `Cli/ReadBackRead.cs` of `8fd0ffc2` restored (no model hash passed, so no acceptance), the two acceptance
tests fail and the 4 binding controls and the plenum control pass (`revision-7/test-summaries.txt`). In that A/B the
wrong optical value still fails, exit 10.

**Not changed or run:** the acceptance and packing scripts (not run; the committed pack is acc3, pre-L1 and
pre-acceptance). Under `Run-S09-ReadBack.ps1 -Phase acceptance` the approved compare would now exit 14 and the
provisional baseline 12, because the acceptance applies under the approved rules only. The writer suite and benchmark
tests were not re-run: revision 7 changes no writer, importer or benchmark file.

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
   - Infrastructure checks as in S08: a different process, the reader started after the export, the OSM and IDF read
     are the exported ones, and the frozen model and EPW are byte-identical. A failed check exits 70.
   - Revision 4: a process is its id and OS creation time, both recorded. An unrecorded identity fails.
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
   - zone and subsurface multipliers, each against the one OSM object of the IDF object's name (revision 3), and the
     building north axis;
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

**How the S09 code handles the two coordinate systems** (`NativeModel.Vertices`, `OsComparison.Orientation`):
- **Relative:** the reader adds the zone origin to each vertex. A zone with a non-zero Direction of Relative North is
  not read; the rotation check below FAILs a non-zero Building North Axis + Direction of Relative North.
- **World** (or its synonym `Absolute`): **supported.** The reader takes the vertices as absolute, with no zone origin
  and no rotation, and compares them like Relative ones. Only a non-zero Building North Axis or Zone Direction of
  Relative North under World is refused (BLOCKED technical), because EnergyPlus would not apply it to the geometry.
- Any other or missing coordinate system is BLOCKED technical. Both fixtures use Relative, so the World path is
  exercised only by the refusal test (`world-with-north-axis`), not by a compared model.

**Rules.** A blank field is read as its documented default, never assumed.

| Native finding | Outcome |
|---|---|
| An effective multiplier ≠ 1 (Zone, ZoneGroup, FenestrationSurface:Detailed) | Consistency → **FAIL**: contradicts SAM |
| Relative coordinates with Building North Axis + Zone relative north ≠ 0, where SAM states no rotation | Consistency → **FAIL** |
| The OSM and IDF disagree (OS:ThermalZone / OS:SubSurface Multiplier, OS:Building North Axis) | Consistency → **FAIL** |
| (revision 3) An IDF Zone or FenestrationSurface:Detailed has no OS:ThermalZone / OS:SubSurface of its name, and every OSM object of that class has a readable Name | Consistency → **FAIL**: a proven missing counterpart |
| An unreadable or non-integer multiplier; an unreadable north axis; an unknown or missing coordinate system; World coordinates with a non-zero Building North Axis or Zone Direction of Relative North; a non-identity OS:Space transformation; a non-zero SAM North Angle (S07 has no orientation tolerance) | `Unsupported` → **BLOCKED technical** (exit 11), never PASS |
| (revision 3) An ambiguous OSM counterpart (two objects of the name), or an OSM object of the class whose Name is unreadable; an OSM Multiplier field that is missing or labelled otherwise | `Unsupported` → **BLOCKED technical** (exit 11) |
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

## Revision 3: missing OSM counterparts (independent review of `6f7e6b7d`)

**Finding.** `OsComparison.Multiplicity` looked up each IDF `Zone` and `FenestrationSurface:Detailed` in the OSM by
name and compared the multipliers only when an object was found. When the `OS:ThermalZone` or `OS:SubSurface` was
absent, the check was silently skipped. The review removed the object from a copy of the box `model.osm`, updated the
manifest's OSM hash and ran a fresh read and compare. The report stayed at exit 12, 316/316, with unchanged outcome
counts and no Consistency or Unsupported entry. A second, related gap: the OSM Multiplier was found by label search,
and a missing label was read as the default 1.

**Change (`Compare/OsComparison.cs`, local to `Multiplicity`; no reader or core change).**
- `OsmCounterpart(type, …, name)` reads the Name of every OSM object of the class at its known position (field 1,
  after the handle) under the file's own label:
  - exactly one match, and every Name readable: the counterpart, whose multiplier is compared as before;
  - no match, and every Name readable: proven missing → Consistency, `multiplicity: IDF Zone '…' has no OS:ThermalZone
    in the OSM (none of its N OS:ThermalZone object(s) has that name); the OSM and IDF disagree` (the same for
    subsurfaces) → **FAIL**;
  - two or more matches, or any Name unreadable (blank, or a field labelled otherwise): the identity is not proven →
    `Unsupported` → **BLOCKED technical**.
- `OsmMultiplier` reads the OSM Multiplier at its known position (`OS:ThermalZone` field 2, `OS:SubSurface` field 8)
  under its label, with the IDF reader's own `NativeModel.Number`. **Blank stays the documented default 1.** A missing
  field, or one labelled otherwise, is an unknown layout → BLOCKED technical; before, it defaulted to 1.
- The multiplicity Supporting line now states the matches: box `1 IDF zone(s) (1 with their OS:ThermalZone) …
  2 IDF subsurface(s) (2 with their OS:SubSurface)`; two-zone `2 (2) … 1 (1)`. It claims "every effective multiplier
  is 1, as SAM states" only when no multiplicity Consistency or Unsupported entry exists, as before.
- The translator keeps the names (IDF `SAM_ThermalZone_FixBox_5080c000` = OSM `OS:ThermalZone` Name), so a name match
  is the identity. Names compare case-insensitively, as EnergyPlus does.

**Negatives** (`Cli/Negative.cs`, also in `Run-S09-ReadBack.ps1 -Phase acceptance-negative`; the IDF is unchanged):
- `neg-osm-missing-thermal-zone`: the first IDF zone's `OS:ThermalZone` removed (box: the only zone; two-zone: zone A).
- `neg-osm-missing-subsurfaces`: every IDF subsurface's `OS:SubSurface` removed (box 2, two-zone 1).

**Before and after** (`documentation/S09-OpenStudio-InputReadBack-evidence/revision-3/`; the same inputs, each binary
in fresh processes; the IDF unchanged in every input):

| Input | Reviewed head `6f7e6b7d` | Revision 3 `c1e53328` |
|---|---|---|
| s08-fix-box, unchanged | BLOCKED owner, exit 12, 316/316 | the same; outcome counts and `report-approved.txt` identical |
| s08-fix-two, unchanged | BLOCKED owner, exit 12, 586/586 | the same; outcome counts and `report-approved.txt` identical |
| box, every `OS:ThermalZone` removed (the reviewer's method) | exit 12, 316/316, no Consistency or Unsupported | **FAIL, exit 10**, 316/316, 1 Consistency |
| box, every `OS:SubSurface` removed (reviewer's method) | exit 12, the same | **FAIL, exit 10**, 2 Consistency |
| two-zone, every `OS:ThermalZone` removed | exit 12, 586/586, the same | **FAIL, exit 10**, 2 Consistency |
| two-zone, every `OS:SubSurface` removed | exit 12, the same | **FAIL, exit 10**, 1 Consistency |
| `neg-osm-missing-subsurfaces` (box) | exit 12 | **FAIL, exit 10**, 2 Consistency |
| `neg-osm-missing-thermal-zone` (two-zone, zone A) | exit 12 | **FAIL, exit 10**, 1 Consistency |

In every FAIL the outcome counts and coverage are those of the unchanged fixture, there is no Unsupported entry, and
every Consistency entry is a missing-counterpart entry.

## Revision 4: final-head CI failure (Build 39603021836 at `e0a6ea19`)

Evidence: `documentation/S09-OpenStudio-InputReadBack-evidence/revision-4/` (README, CI excerpt, scripts, outputs).

**The failed run (preserved, not re-run).** Build (Windows) 39603021836, job 119232047962, on `pull/33/merge` of
`e0a6ea19`. Dependencies: SAM, SAM_SQLite and SAM_Validation at `sow/2026-Q4`, SAM_Tas `c50e9169` (`sow/2026-Q4`).
- Benchmark tests: 51/51. S09 tests: 57/58.
- Failed: `EveryTargetedMutation_Fails("neg-people-density","s08-fix-box")` [695 ms], `DirectoryNotFoundException` on
  `s09-runs\neg-people-density-b0da74\compare-approved\report.json` (`TestSupport.cs:111`).
- SPDX run 39603021804 passed. No artifact was kept.
- **Disposition:** superseded by revision 4. The cause is diagnosed as likely, not proven (below).

**What the run shows.** `compare` returned before `OsReport.Save`, which creates `compare-approved` first. That
happens only for exit 64 (usage) or exit 70 (an infrastructure check, or an exception caught in `Program.Main`). The
test loaded `report.json` before checking its exit code, so the exit code and stderr were lost. Which branch ran is
therefore not known from CI.

**The defect found.** The freshness checks compared process ids only:
- reader id = export id → "the reader did not run in a fresh process after the export";
- compare id = reader id → "the comparison runs in the reader's process".

Windows reuses the id of an exited process. A negative copy keeps the id of the one-time export, whose process exited
long before, so a later fresh reader can carry it. Measured here: 141 of 1000 sequential harness launches got an id an
earlier launch already had, at least 164 launches apart (`pid-reuse-1000.txt`). It depends on load: 0 of 200 in
another run. It was not measured on a GitHub runner.

**Reproduction.** `simulate-pid-reuse.ps1` makes the reused-id state by writing the reader's id into a negative copy's
`export.json`; the reader started after the export finished.
- Reviewed head build (`5041645c…`): 3/3 exit 70 with the message above and no `compare-approved`. This is the CI
  signature.
- Revision 4 (`25a35b92…`): 3/3 FAIL, exit 10, with `report.txt` byte-identical to the unmutated controls and to acc3.

**Conclusion.** The id-only check is a defect, and it is the only mechanism found that produces the CI failure. That it
caused this run is likely but unproven. An exception inside `compare` is not excluded by CI evidence. Locally, none of
319 earlier test runs and no acc3, acc5 or acc6 run lacked a report.

**Change (`0c232875`).**
- `ProcessIdentity`: a process is its id **and** OS creation time.
  - `export.json` `s09-export/2` and `native.json` `s09-native/2` record `ProcessStartedUtc`.
  - Same id and creation time = the same process. The reader must still start after the export finished.
  - A missing identity fails closed (exit 70), so `/1` and hand-made files are refused.
  - `report.json` Inputs gain the three creation times; `report.txt` is unchanged.
- `TestSupport.ReadAndCompare` checks that `report.json` exists before parsing it. A failed `read` or `compare` reports:
  - the exit code and labelled stdout/stderr;
  - both process identities and times;
  - the run's files.

  The same text goes to `s09-diagnostics/*.txt`. The Build workflow uploads those files only on failure (30 days).
- 4 tests:
  - `AReusedProcessId_IsNotTheExportProcess`: FAIL, exit 10, with a report. It fails against the reviewed head's check.
  - `AReadNotProvenFresh_IsAnInfrastructureFailure`: the same process, an unrecorded identity and a read before the
    export finished each give exit 70 and no report.
- Not changed: the hash checks, fixtures, weather, tolerances, the shared core, the writer, and every existing negative
  and assertion. No retry was added.

## Revision 6: fail-closed guards in the lighting read-back (independent review of `31af6022`)

Tested content `225881f00971c7cab3596e94867924a11866e147` (tree `47600688`). Only `OsComparison.LightingSplit` and its
regressions change; the writer, the importer, the fixtures, the tolerances and the shared core do not.

**Gap 1: native domain and energy conservation** (`LightsDomainProblem`).
- The reviewed head compared each row within its S07 tolerance only. A split shifted by 0.0004 per field passes every
  row (the 4c tolerance is 0.001), although on p = 1 it sums to 1.0008 and on p = 0 Fraction Radiant is −0.0004. Both
  reviewer artifacts gave exit 12 with lighting accepted.
- Now, before any comparison: Return Air, Radiant and Visible must each lie in [0, 1], and Return Air + Radiant + Visible
  must not exceed 1 (S07 §5.1: "A sum above 1 is an input error"; it is a negative convective share).
- A violation is an EXPORT-DEFECT on all five lighting rows, FAIL (exit 10). The reason names the field or sum and
  says that no tolerance applies; the rows carry no numeric comparison.
- **Floating-point boundary:** `OsQuantities.LightingFractionBoundary` = 1e-12. A fraction passes in [−1e-12, 1 + 1e-12]
  and a sum at most 1 + 1e-12. This absorbs only the decimal-to-double rounding of the IDF text (under 1e-15 here). It is
  1e9 times smaller than the 4c tolerance, is applied before and never instead of the S07 comparison, and changes no
  tolerance. The writer's p = 1 split passes.
- Unreadable or NaN fields are left to the existing row evaluation (UNVERIFIED or EXPORT-DEFECT).

**Gap 2: plenum-calculated return air** (`LightsPlenumProblem`, IDF `Lights` index 12, after End-Use Subcategory).
- Blank, absent or `No` (the EnergyPlus default, which the writer leaves) is the static split, unchanged.
- `Yes` makes the return-air share follow the plenum temperature at run time, so no static T-LIGHT-SPLIT can be
  verified. Any other value is uninterpretable. A field whose own label differs is unreadable.
- In each of those three cases, all five lighting rows read as unreadable with the reason: UNVERIFIED, BLOCKED technical.
  The transformation is not marked verified, and `Unsupported` gets `lighting: space:<guid>: Lights '<name>' …`. The
  result is exit 11, never PASS.
- Precedence: an invalid domain (FAIL) outranks the plenum block; both are named.

**Regressions** (S09 tests; the native mutations are new CLI `negative` cases):

| Test | Case | Required |
|---|---|---|
| `TheBoundarySplits_PEqualOneAndZero_AreValid` (p = 1, p = 0) | writer exports, unchanged | exit 12, every lighting row passes, no Unsupported |
| `AnInvalidNativeSplit_FailsBeforeAnyTolerance` | `neg-lighting-sum-above-one` on p = 1; `neg-lighting-radiant-to-visible` on p = 0 | exit 10; exactly the 5 lighting rows EXPORT-DEFECT with the named violation; none passes; no comparison made |
| `APlenumCalculatedOrUninterpretableReturnAir_BlocksTechnically` | `neg-lighting-plenum-yes`, `-unknown` (`Maybe`), `-relabelled` | exit 11; one named Unsupported entry; the 5 lighting rows UNVERIFIED, BLOCKED technical; none passes |
| `AnExplicitNoPlenumSetting_IsTheUnchangedControl` | `ctl-lighting-plenum-no` (No with coefficients) | the box's exit code and outcome counts, no Unsupported, the same lighting outcomes |

- **A/B:** with the reviewed `OsComparison.cs` (`31af6022`) restored over `225881f0`, the 5 regressions fail (exit 12,
  where 10 or 11 is required) and the 3 controls pass (`revision-6/test-summaries.txt`).
- **Reviewer artifacts** (machine-local, copied, read and compared fresh with the revision-6 harness;
  `revision-6/review-artifacts-ab.txt`): control and p1-control unchanged (exit 12, identical counts); p1-invalid-sum
  and p0-negative-fraction exit 10 with 5 named EXPORT-DEFECT; dynamic-plenum exit 11 with 5 UNVERIFIED and the named
  Unsupported entry.
- **Fixtures:** both `report.txt` files are byte-identical to `revision-5/`, and the IDFs keep their hashes.
- The new CLI cases are not added to `Run-S09-ReadBack.ps1 -Phase acceptance-negative`, which requires exit 10 on the
  unchanged fixture runs. The plenum cases exit 11, and the two partition shifts are invalid only on p = 1 or p = 0
  exports; on the fixtures they stay valid and within tolerance.

## Revision 5: the S07 §5.1 lighting split (amendment L1)

Tested content `20f8fb659a26b69173ebc98c75adf2a037ae7661` (tree `1f4f1844`). Contract: SAM S07 §5.1 and T-LIGHT-SPLIT,
merged by SAM#217 as `113873a0`. Checked first: post-merge Test run 39667707673 (`test (Release)`) and Build (Windows)
run 39667707679 succeeded on `113873a0`; SAM `sow/2026-Q4` is `ba686d24` (the L1 closeout).

**Writer** (`OS/Convert/ToOpenStudio/InternalCondition.cs`, new `OS/Classes/LightingSplit.cs`):
- p = `LightingRadiantProportion`, the total radiant share. `Fraction Radiant` = p·(1 − s), `Fraction Visible` = p·s,
  s = 0.2415 / 0.576 (a C# constant expression, never rounded); `Return Air Fraction` is not set (0). Convective 1 − p.
- **`LightingViewCoefficient` is never read for `Lights`.** When authored it is reported as `SAM-OS-IC-001` information
  ("a TAS comfort ... coefficient, not an energy fraction"), next to the occupancy and equipment lines.
- An authored p, including 0, is preserved. SAM rejects values outside [0, 1] at `SetValue` (`DoubleParameterValue(0, 1)`),
  so no out-of-range p reaches the writer; no extra branch was kept.
- An absent or NaN p takes p₀ = 0.576 (Fraction Radiant 0.3345, Fraction Visible 0.2415). This replaces the previous
  silent defaults 0.32 / 0.25 (the view-coefficient default). See "OD-L1" below.
- **New code `SAM-OS-IC-002`** (`InternalConditionAssumption`), Information at Summary (`GetDiagnosticDetail` Summary
  list; pinned in `DiagnosticsAdapterTests` and `DiagnosticDetailTableTests`). One line per SpaceType:
  - authored: p, the values written, s and the reference category with its "not applicable to" list;
  - unstated: the same, plus that p₀ was applied, that it is not a regulatory, NCM, TM59 or dwelling value, that its
    suitability outside the category is owner decision OD-L1, and to author p where a method prescribes it.
  - A separate code keeps the clean-fixture check "no `SAM-OS-IC-001`" meaningful: the note is an assumption, not an
    unsupported input.
- The occupancy and equipment view-coefficient reasons no longer claim a missing "visible-fraction field" (§5.1). The
  message prefixes, and so their Detailed level, are unchanged. The lighting view-coefficient line stays at Summary
  (it is not added to the inert table), because until now it reached EnergyPlus as Fraction Visible.

**Importer** (`OS/Convert/ToSAM/InternalCondition.cs`), §5.1 default-resolution item 5:
- p = Fraction Radiant + Fraction Visible; `LightingViewCoefficient` is never set.
- Warning `SAM-OSI-APX-001` when Return Air Fraction > 0 (that share becomes convective room gain); information
  `SAM-OSI-APX-001` when F_V / (F_R + F_V) differs from s by more than 1e-6 (not when p = 0).
- Fractions summing above 1 (an EnergyPlus input error): warning, and no p is imported.
- SAM files imported under the previous mapping (F_R in p, F_V in the view coefficient) cannot be identified and are not
  migrated (§5.1).

**S09 read-back** (`Compare/OsComparison.LightingSplit`, `OsQuantities`):
- Five rows per space, from the raw SAM p (`ReadBackExpected` parameters) and the §5.1 constants in `OsQuantities`
  only (no writer reference):

  | Key | Native | Expected | Tolerance |
  |---|---|---|---|
  | `lighting.returnAir` (`4c.os.returnAir`) | Return Air Fraction | 0 | exact |
  | `lighting.radiantTotal` (4c) | F_R + F_V | p | 0.001 |
  | `lighting.visible` (4c) | F_V | p·s | 0.001 |
  | `lighting.radiant` (4c) | F_R | p·(1 − s) | 0.001 |
  | `lighting.convective` (4c) | 1 − F_RA − F_R − F_V | 1 − p | 0.001 |

- A blank field of a present `Lights` object reads as its IDD default 0 (S07: "blank reads as 0"), so a blank cannot
  pass as native absence for an unstated p.
- An authored p is verified T-LIGHT-SPLIT: ACCEPTED-TRANSFORMATION, or MATCH where SAM and native are both 0 (the core
  rates S-ZERO with a zero native MATCH; both are PASS).
- An absent or NaN p is S-UNSPEC on every row, with the p₀ split only as the listed default: DOCUMENTED-DEFAULT (rank 3,
  BLOCKED owner), never PASS. Return Air reaches it through the approved G1 rule (zero is its listed default).
- A p outside [0, 1] would be S-INVALID (defensive; SAM's validation prevents it).
- Removed: the `lighting.visible` comparison against `LightingViewCoefficient` (listed default 0.25) and the test
  exception for its DOCUMENTED-DEFAULT.

**Fixture results (S09 test runs at `20f8fb6`; `revision-5/`):**

| Fixture | Status (exit) | Coverage | Outcomes | Non-PASS |
|---|---|---|---|---|
| `s08-fix-box` (p 0.37) | BLOCKED owner (12) | 319/319 | Match 220, WithinTolerance 25, AcceptedTransformation 4, NotApplicable 1, MappingDifference 69 | OS-MAT-SINGLE-SIDED (69) only |
| `s08-fix-two` (p 0.34, 0.41) | BLOCKED owner (12) | 592/592 | Match 393, WithinTolerance 46, AcceptedTransformation 14, NotApplicable 1, MappingDifference 138 | OS-MAT-SINGLE-SIDED (138) only |

- The IDFs differ from the revision-4 acceptance `acc6` only in the `Lights` Fraction Radiant and Fraction Visible
  lines (`revision-5/idf-diff-acc6.txt`). Native F_V / F_R: box 0.155130 / 0.214870; A 0.142552 / 0.197448; B
  0.171901 / 0.238099, the §5.1 expected vectors.

**New S09 tests (18):**
- the authored p of each fixture space against the §5.1 vectors (3);
- an authored 0: four rows MATCH at 0, convective 1 (1);
- an authored view coefficient (0.49, 0): every lighting row, the coverage and the outcome counts unchanged (2);
- an absent and a NaN p: exactly the five lighting rows are DOCUMENTED-DEFAULT, BLOCKED owner, S-UNSPEC (2);
- 5 negatives × 2 fixtures, each FAIL (exit 10) with only lighting rows failing and no OSM/IDF disagreement (10):
  `neg-lighting-previous-mapping` (F_R = p, F_V = 0.25), `neg-lighting-visible-zero` (F_V = 0 with p > 0),
  `neg-lighting-radiant-is-p` (F_R = p with F_V > 0), `neg-lighting-return-air` (0.1), `neg-lighting-swapped`.
- Updated: the two-zone ACCEPTED-TRANSFORMATION count 6 → 14.

**New writer and importer tests** (`tests/.../C2/LightingSplitTests.cs`, 16): the §5.1 vectors; an authored 0; absent
and NaN p → p₀ with the Summary note naming OD-L1; the view coefficient (0.49, 0.25, 0, 1) never changes the fractions
for p 0.37, 0 and absent; import of p₀, of 0, of another short-wave share, of a nonzero return air, of a sum above 1;
round trips of 0.37 and 0 with a view coefficient. Updated: `RadiantProportions_ComeFromSamParameters` (the split), the
D1 detail table, the released-code table and the real-converter detail test.

**A/B:** the previous writer and importer restored over `20f8fb6`: 18 writer tests and 21 S09 tests fail
(`revision-5/test-summaries.txt`).

**Not changed:** fixtures, weather, tolerances, the shared core, ventilation, opaque optics, glazing, the occupancy and
equipment radiant defaults (0.3, 0), SAM_Tas, SAM_LadybugTools. One wording change: the note says "the entire fitting"
because `FenestrationTests.Holes_...` takes the first diagnostic containing "hole" ("whole" matched it).

## Decisions and assumptions (for review)

1. **T-VENT-INACTIVE for OpenStudio.**
   - It applies the S08 owner decision 3 preconditions unchanged, under the S09 owner policy (V0/V1 deliver nothing):
     - no selection;
     - the DSOA is present with its requirement;
     - its fraction schedule is read in both the IDF and the OSM, hour-labelled and exactly 0 for all 8760 hours;
     - every delivered flow is exactly 0;
     - approved rules only.
   - It covers rows 6 (ventilation) and 8b only.
   - Without it, V1 would be the G1 UNLISTED-DEFAULT (BLOCKED owner). **Confirmed by the owner on 2026-10-22
     (ventilation A; see "Revision 7").**
2. **8a per basis:** an unstated basis is S-ZERO once any basis is stated. The S07 8a requirement is the sum, and the core's
   `ExpectedRequiredSupplyFlow` adds nothing for it. In V0 it stays S-UNSPEC. The summed requirement is also compared
   (`ventilation.requirement.total`).
3. **OS outdoor-air precedence (S07 section 3):** the override replaces the condition bases. For an override, SAM's
   requirement is its flow and the other bases are S-ZERO. Tas ignores `OutsideSupplyAirFlow`, so a model with an
   override diverges between the engines. The fixtures have none.
4. **Single-sided opaque optics (OS-MAT-SINGLE-SIDED):**
   - EnergyPlus `Material` has one solar, visible and thermal absorptance for both faces. Until revision 7 the writer
     wrote the external value everywhere; revision 8 writes SAM's first layer with its internal face (see "Revision 8").
   - S09 compares every used face exactly and lists a row MAPPING-DIFFERENCE only on a face EnergyPlus does not use,
     and only while the native value is the layer's other authored face. Rank 3; listed by S09, not by S07.
   - Revision 7 recorded the owner's option-1 acceptance (exit 14); revision 8 removes it on the owner's instruction to
     correct the mismatch. The remaining unused-face rows need an owner decision (accept, or an S07 3a scope decision).
5. **Lighting split (revision 5; supersedes the "lighting visible fraction" item of revisions 1–4):** S07 amendment L1
   withdrew the view coefficient ↔ Fraction Visible identity, so that item is no longer an owner decision. The fixtures
   author p and pass T-LIGHT-SPLIT.
   - **OD-L1 interpretation (confirmed by the owner on 2026-10-22, lighting B1; OD-L1 itself stays open):** §5.1 and §8 gate only the **SAM_Tas** fallback on OD-L1, and
     list "absent p → p₀ with the diagnostic" among the required SAM_OpenStudio#33 changes under the approved D-LIGHT.
     The OpenStudio writer has no template value to replace; it had silent defaults 0.32 / 0.25. Revision 5 therefore
     applies p₀ for an unstated p, states it at Summary with the category, its limits and OD-L1, and claims no
     suitability outside the category. If the owner reads OD-L1 as also gating this fallback, the change is the
     `!lightingRadiantProportionStated` branch of the writer only; the authored-input corrections stand alone.
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
10. **OSM counterpart identity (revision 3):** the IDF object's name is its OSM identity, because the translator keeps
    names. Consistency is claimed only against exactly one OSM object of that name, read under the file's labels. "No
    such object among readable names" is proven missing (FAIL); anything less certain blocks. Only the IDF → OSM
    direction is checked: an OSM zone or subsurface with no IDF object is not looked for (see "Unresolved").

## Fixture verdicts (approved rules)

**Revision 5 supersedes the counts and the lighting item below** (319/319 and 592/592, optics only; see "Revision 5"),
and revision 7 the status: both fixtures are NOT PASS, accepted limitation, exit 14 (see "Revision 7").
This section is the revisions 1–4 acceptance (`acc3`/`acc6`), which predates amendment L1 and was not re-run.

Evidence: `documentation/S09-OpenStudio-InputReadBack-evidence/`, revision-3 acceptance `acc3`. Harness
`os-input-readback.dll` `b7cf9f26…`; library `ce8270dd…` (revision 2 `acc2`: `32090bbf…` / `5f279f12…`). The
revision-4 acceptance `acc6` (`25a35b92…` / `006668df…`) gives the same table, with every `report.txt` and IDF
byte-identical to acc3; the committed pack stays acc3.

| Run | Status (exit) | Coverage | Outcomes | Non-PASS (all owner decisions) |
|---|---|---|---|---|
| `s08-fix-box` (V2) | BLOCKED owner (12) | 316/316 | Match 220, WithinTolerance 25, NotApplicable 1, MappingDifference 69, DocumentedDefault 1 | OS-MAT-SINGLE-SIDED (69 rows), lighting visible fraction |
| `s08-fix-two` (V1, V1) | BLOCKED owner (12) | 586/586 | Match 393, WithinTolerance 46, AcceptedTransformation 6, NotApplicable 1, MappingDifference 138, DocumentedDefault 2 | OS-MAT-SINGLE-SIDED (138 rows), lighting visible fraction (2 zones) |
| `box-repeat` | BLOCKED owner (12) | 316/316 | identical report | IDF byte-identical |

- Every `report-approved.txt` is byte-identical to revision 1's and revision 2's. Revision 2 changed only the JSON
  (schema `/2`, the orientation and multiplicity Supporting lines). Revision 3 changes only the multiplicity Supporting
  line (the counterpart counts), plus fresh OSM hashes, DLL hashes, PIDs and times. The acc3 IDFs are byte-identical to
  acc2's; the OSM bytes differ between exports (random handles), as before.
- **Ventilation:**
  - Box V2: 8b passes for all 8760 hours, and the 6.hourly ventilation schedule matches.
  - Two-zone: both zones are V1, with the requirement read back and matched (8a). 8b and the ventilation schedule pass as
    T-VENT-INACTIVE.
- **Hourly series:** all 11 per fixture zone (5, 6, 8b) cover 8760 hours with alignment verified from the native labels.
  The calendar is RunPeriod 2006 (non-leap), 1/1-12/31, with an 8760-row EPW.
- **Placement:** both fixtures unrotated (Relative coordinates, Building North Axis blank, no zone rotation, SAM North
  Angle unstated). All multipliers are 1: box 1 zone and 2 subsurfaces, two-zone 2 zones and 1 subsurface, no ZoneGroup.
  Each IDF zone and subsurface has exactly one OSM object of its name, whose blank Multiplier is the documented
  default 1.
- **Provisional rules:** the same exit codes (baseline).

**Negatives** (`neg-*`; each a disposable copy, read and compared fresh). All 16 FAIL (exit 10), and the originals are
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
| osm-missing-thermal-zone (two-zone in acceptance; both fixtures in the tests) | OSM-only: the first IDF zone's OS:ThermalZone removed | FAIL (missing OSM counterpart only) |
| osm-missing-subsurfaces (box in acceptance; both fixtures in the tests) | OSM-only: every IDF subsurface's OS:SubSurface removed | FAIL (missing OSM counterpart only) |

## OS-MAT-SINGLE-SIDED: optics decision table (option 1 accepted 2026-10-22, the two reviewed fixtures only)

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
- **Owner options.** The owner chose option 1 on 2026-10-22, for the two reviewed fixtures only, with result effects
  unmeasured. Options 2 and 3 are not authorised and stay available as separate tasks.
  1. accept OS-MAT-SINGLE-SIDED as a limitation for both fixtures (rank 5, never PASS) — **chosen**;
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

1. **Tas-backed evaluators (SAM_UI WPF).** In the four WPF workflows traced here, the U, g and LT that SAM_UI shows
   or stores come from Tas:
   - the U-value window (`UI/Windows/SetUValueWindow.xaml.cs` → `TasUValueEvaluator`);
   - the glazing window (`UI/Windows/SetGlazingWindow.xaml.cs` → `TasGlazingEvaluator`);
   - the thermal edit services (`UI/Classes/Thermal/ThermalEditSession.cs`, `ThermalEditServices`), whose default
     factories create all four evaluators; construction alternatives (`ConstructionAlternatives.cs`) evaluate through
     `TasConstructionUValueEvaluator`;
   - the Glazing Builder (`UI/Classes/GlazingBuilder/GlazingBuilderViewModel.cs` → `DraftGlazingEvaluator`).

   Other routes were not traced, so nothing is claimed for them: Grasshopper components (which use
   `SAM.Analytical.Glazing`, item 2, for g and τv), values imported from Tas files, library or user-entered values, and
   any other SAM or SAM_UI command.
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
  - **Short-array guard defect (a code defect, proven by reading).** `ST/Classes/ThermalTransmittanceCalculator.cs:466-468`
    (and `:508-510`) guards `values.Length >= 6` and then reads `values[6]`. A 6-element array therefore throws
    `IndexOutOfRangeException` instead of being refused; the guard needs `>= 7`. `ST/Modify/Run.cs` catches every
    exception, so the evaluation ends without a Ug, and the UI shows Ug ≤ 0 as "Not calculated".
  - **The reported Ug display issue (cause not established).** SAM_UI `documentation/Thermal-StageE0-1.md:123` records
    Ug left at 0. Tas `GetUValue()` normally returns 7 values, and the guard defect above can produce this only if Tas
    returns exactly 6. Whether it ever does, or whether another exception swallowed by `Run.cs` is the cause, is
    unknown. Establishing the cause needs a licensed Tas reproduction, which S09 does not authorise. Fixing the guard is
    worthwhile on its own but is not shown to fix the display issue.
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
- Revision 3 changed:
  - `Compare/OsComparison.cs` (`Multiplicity`, the new `OsmCounterpart`, `OsmMultiplier`), `Run-S09-ReadBack.ps1`
    (2 negative cases);
  - `Cli/Negative.cs` (2 cases, `RemoveObject`), `Tests/ReadBackTests.cs` (8 tests);
  - the evidence `runs/` JSON, `summary-*.json` and 2 new `negatives/` folders (repacked from acc3; every existing
    `report-*.txt` is unchanged), and the new `revision-3/` folder;
  - this record.

  No writer, reader (`Native/`), report schema, shared-core, fixture, weather or tolerance change.
- Revision 4 changed:
  - `Classes/ExportManifest.cs` (`ProcessIdentity`, `ProcessStartedUtc`, schema `/2`), `Native/NativeReader.cs` (the
    reader's `ProcessStartedUtc`, schema `/2`);
  - `Cli/Export.cs`, `Cli/ReadBackRead.cs` (the infrastructure checks, three report inputs);
  - `Tests/TestSupport.cs` (failure evidence, `Compare`), `Tests/ReadBackTests.cs` (4 tests);
  - `.github/workflows/build.yml` (the diagnostics upload on failure);
  - the new `revision-4/` evidence folder and this record.

  No writer, comparison (`Compare/`), report text, shared-core, fixture, weather or tolerance change. The committed
  `runs/`, `negatives/` and `summary-*.json` stay the acc3 pack.
- Revision 5 changed:
  - writer: `OS/Classes/LightingSplit.cs` (new), `OS/Convert/ToOpenStudio/InternalCondition.cs`; importer:
    `OS/Convert/ToSAM/InternalCondition.cs`; `SAM.Core.OpenStudio` `OpenStudioDiagnosticCodes.cs` (`SAM-OS-IC-002`),
    `Query/GetDiagnosticDetail.cs`;
  - S09: `Compare/OsComparison.cs`, `Compare/OsQuantities.cs`, `Cli/Negative.cs` (5 cases), `Run-S09-ReadBack.ps1`
    (the 5 cases listed; not run), `Tests/ReadBackTests.cs`;
  - writer tests: `C2/LightingSplitTests.cs` (new), `C2/InternalConditionCompletenessTests.cs`,
    `D1/DiagnosticDetailTableTests.cs`, `D1/DiagnosticsAdapterTests.cs`;
  - `docs/SAM_OPENSTUDIO_INTERNAL_CONDITION_MAPPING.md`, `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md`,
    `docs/SAM_OPENSTUDIO_TO_SAM_COVERAGE.md` (totals Native 56, Derived 12), `tests/resources/openstudio-analytical-coverage.json`,
    `tests/resources/openstudio-to-sam-coverage.json`;
  - the new `revision-5/` evidence folder and this record.

  No fixture, weather, tolerance, shared-core, ventilation or optics change. The committed `runs/`, `negatives/` and
  `summary-*.json` stay the acc3 pack (pre-L1).
- Revision 6 changed:
  - `Compare/OsComparison.cs` (`LightingSplit`, `LightsDomainProblem`, `LightsPlenumProblem`), `Compare/OsQuantities.cs`
    (`LightingFractionBoundary`);
  - `Cli/Negative.cs` (6 cases, `AppendFields`), `Tests/ReadBackTests.cs` (8 tests);
  - the new `revision-6/` evidence folder and this record.

  No writer, importer, report schema, shared-core, fixture, weather, tolerance, ventilation or optics change.
- Revision 7 changed:
  - `Compare/OwnerDecisions.cs` (new), `Compare/OsComparison.cs` (`Run`: the acceptances and the OS-MAT-SINGLE-SIDED
    Supporting line), `Cli/ReadBackRead.cs` (the verified model hash passed to `Run`);
  - `Tests/ReadBackTests.cs` (1 test changed, 5 added, 1 message filter);
  - the new `revision-7/` evidence folder and this record.

  No writer, importer, report schema, exit code, shared-core, SAM_Tas, fixture, weather, tolerance, ventilation or
  lighting change.
- Revision 8 changed:
  - writer: `OS/Convert/ToOpenStudio/Material.cs` (`ToOpenStudio_Layer`, face variants, metadata),
    `Construction.cs` (face by SAM layer position, `SingleLayerDivergentFaceOptics`), `AnalyticalModel.cs` (per-use
    single-layer error, `ForwardSideProblem`, `IsZoneFaceOnlyBoundary`); `SAM.Core.OpenStudio`
    `OpenStudioDiagnosticCodes.cs` (`SAM-OS-MAT-003`), `OpenStudioIdentityKeys.cs` (face keys);
  - importer: `OS/Convert/ToSAM/MaterialLibrary.cs` (`StampedFaceOptics`), `OS/Classes/OpenStudioImportContext.cs`;
  - S09: `Compare/OsComparison.cs` (used-face comparison, `SingleLayerFaces`; `Run` as at revision 6),
    `Cli/ReadBackRead.cs` (as at revision 6), `Compare/OwnerDecisions.cs` deleted, `Tests/ReadBackTests.cs`;
  - writer tests: `M4/OpaqueFaceOpticsTests.cs` (new, 21), `C7/DeclaredDiagnosticsTests.cs`,
    `D1/DiagnosticDetailTableTests.cs`, `D1/DiagnosticsAdapterTests.cs`;
  - `docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md`, `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md` (Derived 31, Approximated
    18), `tests/resources/openstudio-analytical-coverage.json`;
  - the new `revision-8/` evidence folder and this record.

  No fixture, weather, tolerance, report schema, shared-core, SAM_Tas, ventilation, lighting or glazing change.
- Revision 9 changed:
  - writer: `OS/Convert/ToOpenStudio/Material.cs` (`InheritedInternalOptics`, SAM-OS-MAT-004, written stamps);
    `SAM.Core.OpenStudio` `OpenStudioDiagnosticCodes.cs` (`SAM-OS-MAT-004`), `OpenStudioIdentityKeys.cs`
    (`WrittenPrefix`), `Query/GetDiagnosticDetail.cs` (MAT-004 at Summary);
  - importer: `OS/Convert/ToSAM/MaterialLibrary.cs` (`ReadFaceObject`, `ApplyEdits`, `MergedOpaqueMaterial`),
    `OS/Classes/OpenStudioImportContext.cs` (`OpaqueMaterialGroups`);
  - S09: `Compare/OsComparison.cs` (`ExpectedLayerValue`: NaN is unstated), `Tests/ReadBackTests.cs` (2 cases);
  - writer tests: `M4/OpaqueFaceOpticsTests.cs`, `D1/DiagnosticDetailTableTests.cs`, `D1/DiagnosticsAdapterTests.cs`;
  - `docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md`, `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md`,
    `docs/SAM_OPENSTUDIO_TO_SAM_COVERAGE.md`, `tests/resources/openstudio-analytical-coverage.json`,
    `tests/resources/openstudio-to-sam-coverage.json`;
  - the new `revision-9/` evidence folder and this record.

  No fixture, weather, tolerance, report schema, shared-core, SAM_Tas, ventilation, lighting, glazing, single-layer or
  exposed-face change.

## Tests and validation (local, Release x64)

| Check | At | Result |
|---|---|---|
| S09 tests | `354e532` (revision 9; harness `4edf422b…`, library `1c4408f7…`) | **97/97** (95 + 2 provenance cases); both fixture `report.txt` byte-identical to `revision-8/` |
| Writer/importer, `--filter 'TestCategory!=Simulation'` | `354e532` | **409 passed, 2 skipped** (face-optics tests 30, was 21) |
| Benchmark, `--filter TestCategory!=Simulation` | `354e532` | 51/51 |
| A/B: `Material.cs`, `MaterialLibrary.cs`, `OpenStudioImportContext.cs`, `OsComparison.cs` of `901e592` restored | working tree, then restored and rebuilt | face-optics **9 of 30 fail** (the new regressions), S09 provenance **2 of 2 fail**; controls pass |
| S09 tests | `fc83a6d` (revision 8; harness `44cb7177…`, library `1a4a03df…`) | **95/95**: the revision-7 cases minus its 5 acceptance tests and with the fixture test requiring exit 12, plus 7 optics tests |
| Writer/importer `tests/SAM.Analytical.OpenStudio.Tests`, `--filter 'TestCategory!=Simulation'` | `fc83a6d` | **400 passed, 2 skipped** (379 + 21 new; 2 changed) |
| Benchmark `--filter TestCategory!=Simulation` | `fc83a6d` | 51/51 |
| A/B: writer of `7fccdcc` restored | working tree, then restored and rebuilt | 8 optics tests: **8 failed** (fixtures exit 10, zone faces out of tolerance) |
| A/B: comparator of `8fd0ffc` restored | working tree, then restored and rebuilt | 8 optics tests: **8 failed** (used faces listed; swapped face exit 12, hidden) |
| S09 tests | `614a7a8` (revision 7; harness `e41b13ee…`, library `afc9920c…`) | **93/93**: the 88 revision-6 cases (1 changed to require exit 14) plus 5 |
| The new and changed tests with `Cli/ReadBackRead.cs` of `8fd0ffc2` restored | working tree, then restored and rebuilt | **2 failed, 5 passed**: the two acceptance tests fail (exit 12); the binding and plenum controls pass |
| S09 tests | `225881f0` (revision 6; harness `a05202d0…`, library `727c08bc…`) | **88/88**: the 80 revision-5 cases, unchanged, plus 8 (5 regressions, 3 controls) |
| The 8 new tests with the reviewed `OsComparison.cs` (`31af6022`) restored | working tree, then restored (`cmp` identical) and rebuilt | **5 failed, 3 passed**: exactly the 5 regressions fail (exit 12) |
| The reviewer's 5 artifacts, read and compared fresh | `225881f0` harness | 2 controls unchanged (12); 2 invalid splits FAIL (10); dynamic plenum BLOCKED technical (11) |
| `tests/SAM.Analytical.OpenStudio.Tests` with `--filter 'TestCategory!=Simulation'` | `225881f0` | 379 passed, 2 skipped (no writer or test change) |
| Benchmark tests (not Simulation) | `225881f0` | 51/51 |
| S09 tests | `20f8fb65` (revision 5; harness `3549e421…`, library `b4e03d5f…`) | **80/80**: the 62 revision-4 cases (2 adjusted: no lighting exception, two-zone ACCEPTED-TRANSFORMATION 14) plus 18 lighting cases. Run twice; the second run produced `revision-5/` |
| `tests/SAM.Analytical.OpenStudio.Tests` with `--filter 'TestCategory!=Simulation'` | `20f8fb65` | **379 passed, 2 skipped** (381 = the reviewer's 365 + 16 new). An earlier working-tree run had 1 failure, the "hole" substring collision, fixed before the commit |
| Benchmark tests (not Simulation) | `20f8fb65` | 51/51 |
| A/B: the `6a36afb` writer and importer restored over `20f8fb65` | working tree, then restored and rebuilt | writer 18 failed / 361 passed; S09 21 failed / 59 passed |
| Acceptance scripts | not run (out of revision-5 scope) | the committed pack stays acc3 |
| S09 tests | `0c232875` (revision 4; harness `25a35b92…`, library `006668df…`) | **62/62**: the 58 revision-3 cases, unchanged, plus the 4 process-identity tests |
| The 4 new tests with the reviewed head's id-only check restored | working tree, then restored and rebuilt | `AReusedProcessId_IsNotTheExportProcess` fails (exit 70); the 3 refusal cases pass under both |
| Benchmark tests (not Simulation) | `0c232875` | 51/51 |
| Acceptance and negatives, `C:\TasOut\s09\acc6` | revision-4 harness `25a35b92…` | as in "Fixture verdicts"; 16/16 negatives FAIL; originals unchanged; all 19 `report.txt` and the 3 IDFs byte-identical to acc3 |
| Reused-id A/B, `C:\TasOut\s09\rev4-ab` | `e0a6ea19` source vs `0c232875` | see "Revision 4" |
| `tests/SAM.Analytical.OpenStudio.Tests` (writer regressions) | not re-run: revision 4 changes no writer or writer-test file | 388 passed, 2 skipped at revision 3 (unfiltered scope; see "Writer-test scope correction") |
| S09 tests (export in-process; read and compare in fresh processes) | `c1e53328` (revision 3) | **58/58**: the 50 revision-2 cases, unchanged in what they assert (the BLOCKED-technical test's "mutation applied" check now also covers the OSM), plus 8 new cases: 4 missing-counterpart FAILs (both cases × both fixtures), 1 fixture counterpart count, and 3 cases added to the BLOCKED-technical test (duplicated OS:ThermalZone, unlabelled OS:ThermalZone Name, relabelled OS:SubSurface Multiplier) |
| The same 58 tests with the reviewed head's `OsComparison.cs` (`6f7e6b7d`) | working tree, then restored and rebuilt `--no-incremental` | **8 failed, 50 passed**: exactly the 8 new tests fail |
| Acceptance and negatives, `C:\TasOut\s09\acc3` | revision-3 harness | 3 runs as in "Fixture verdicts"; 16/16 negatives FAIL; originals unchanged; acc3 IDFs byte-identical to acc2 |
| Before/after on the same inputs, `C:\TasOut\s09\rev3-ab` | `6f7e6b7d` vs `c1e53328` | see "Revision 3" |
| `tests/SAM.Analytical.OpenStudio.Tests` (writer regressions) | revision-3 working tree (= `c1e53328`; no writer or test change) | 388 passed, 2 skipped (390), as at revision 2 (Total 390 = no category filter; see "Writer-test scope correction") |
| `benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests` (not Simulation) | same | 51/51 |
| S09 tests (revision 2) | `cfe097f4` | **50/50**: the 33 revision-1 tests unchanged, plus 17 new: 9 negatives (6 box, 3 two-zone), 3 SAM-intent-only, 4 BLOCKED-technical, 1 fixture placement |
| (revision 2) The same 50 tests with `Orientation()` and `Multiplicity()` disabled (the reviewed head's behaviour) | working tree, then restored | **17 failed, 33 passed**: exactly the new tests fail |
| (revision 2) `tests/SAM.Analytical.OpenStudio.Tests` (writer regressions) | revision-2 working tree (no writer or test change) | 388 passed, 2 skipped (390 = the `[Test]`/`[TestCase]` count at the base `7f216657` and here; revision 1 recorded 363/2, which is superseded) (historical text kept; 363 + 2 = 365 is the filtered count, so "superseded" is withdrawn: see "Writer-test scope correction") |
| (revision 2) `benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests` (not Simulation) | same | 51/51 |
| (revision 2) Acceptance and negatives, `C:\TasOut\s09\acc2` | revision-2 harness | 3 runs as in "Fixture verdicts"; 14/14 negatives FAIL; originals unchanged |
| Before/after on the same inputs, `C:\TasOut\s09\rev2-ab` | `d6d1cc5b` vs revision 2 | see "Revision 2" |
| Revision 1 mutations M1 (V1 delivers fraction 1), M2 (override unscheduled) | `ebd9c4e4` | 5 and 3 tests fail (not repeated) |

```text
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --filter 'TestCategory!=Simulation'
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --filter TestCategory!=Simulation
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance -Root <new dir> -WeatherEpw <budapest.epw>
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance-negative -Root <same dir> -WeatherEpw <budapest.epw>
benchmark/SAM.Analytical.OpenStudio.InputReadBack/Pack-S09-Evidence.ps1 -Root <same dir>
```

### Writer-test scope correction (record only, after the review of `59036783`)

- **The command was unsafe.** Until this correction the writer-test line above had no category filter. The writer
  suite has 25 `[Category("Simulation")]` cases (390 total, 365 without them), so as written it would also select
  the Simulation cases. It now passes `--filter 'TestCategory!=Simulation'`. The benchmark line already filtered; the
  S09 project has no Simulation category. No other S09 reproduction command in this record or the `revision-2/`,
  `revision-3/` or `revision-4/` READMEs runs the writer suite.
- **Historical 388 passed / 2 skipped claims (kept above, not rewritten):**
  - Revision 3: a retained machine-local summary, `C:\TasOut\s09\rev3-ab\other-tests.txt` (not committed), reads
    `Passed: 388, Skipped: 2, Total: 390, Duration: 5 m 45 s`. Total 390 shows the run had no category filter, so the
    25 Simulation cases were in scope. The summary does not show per-test outcomes or whether EnergyPlus was invoked.
  - Revision 2: no log retained. The record's total of 390 indicates the same unfiltered scope; its execution scope is
    otherwise unverified.
  - Revision 4: not run (as stated above).
  - No owner authorisation for a simulation run is recorded for any of these runs, and none is inferred here.
  - Revision 1's 363/2 (365 total) matches the filtered count. The revision-2 note calling it superseded is withdrawn;
    revision 1's exact command is not recorded, so its scope is likewise unverified.
- **The independent reviewer's results at `59036783` (reviewer-run; not run in this session):** S09 tests 62/62;
  benchmark tests excluding Simulation 51/51; writer suite with `--filter 'TestCategory!=Simulation'` 363 passed,
  2 skipped. This filtered writer result is the one that matches the corrected command.

The before/after commands are in `documentation/S09-OpenStudio-InputReadBack-evidence/revision-3/README.md`
(revision 3) and `revision-2/README.md` (revision 2, with the optics commands). Under Windows PowerShell 5.1, pass
`-Exe <path to os-input-readback.exe>` to `Run-S09-ReadBack.ps1`: its `$PSScriptRoot`-based default is empty there
(it was run with an explicit `-Exe` for acc3).

- **Prerequisites:**
  - sibling checkouts `SAM` (built into `SAM/build`) and `SAM_Tas` at `sow/2026-Q4`;
  - the Budapest EPW, decompressed from SAM `SAM/SAM/SAM.Tests/Resources/Weather/HUN_BU_Budapest.Met.Center.128400_TMYx.epw.gz`
    (sha256 `472ffb4e…`).
- **Machine-local** (not needed elsewhere): `C:\TasOut\s09\acc1` (revision 1), `C:\TasOut\s09\acc2` and
  `C:\TasOut\s09\rev2-ab` (revision 2), `C:\TasOut\s09\acc3` and `C:\TasOut\s09\rev3-ab` (revision 3),
  `C:\TasOut\s09\acc6` and `C:\TasOut\s09\rev4-ab` (revision 4). `acc4-discarded-old-check-binary` is not evidence.
- Build first and then run `dotnet test --no-build`: SDK 10.0.401 `dotnet test` rejects `--no-incremental`.
- Building the reference writes ignored `bin/obj` output into the SAM_Tas core folder. The SAM_Tas working tree stays
  clean.
- **Hosted CI** (Build (Windows) and SPDX on each PR head):
  - The Build workflow clones SAM, SAM_SQLite and SAM_Validation, then SAM_Tas sparsely (the core and `ReadBack/`),
    trying the PR head branch name first and then `sow/2026-Q4`. None of them has this branch, so all resolve to
    `sow/2026-Q4`. It logs the SAM_Tas SHA, builds, and runs two test projects: the benchmark producer tests (51) and
    the S09 tests. **It does not run `tests/SAM.Analytical.OpenStudio.Tests`**; the writer regressions above are local
    only.
  - At the revision-2 head `6f7e6b7d`: Build (Windows) run 39598434178 succeeded (SAM_Tas `c50e9169`, `sow/2026-Q4`;
    benchmark 51/51, S09 50/50); SPDX run 39598434210 succeeded.
  - At the revision-3 head `e0a6ea19`: Build (Windows) run 39603021836 **failed** (S09 57/58; see "Revision 4"); SPDX
    run 39603021804 succeeded.
  - From revision 4, a failed Build uploads `SAM_OpenStudio-s09-diagnostics` (the S09 failure text only).
  - The final head and its checks are in the PR description.

## Unresolved, risks, limits

- **Owner decisions in force (2026-10-22):** the T-VENT-INACTIVE extension (ventilation A) and the Decision 5 reading
  (lighting B1). The optics option-1 acceptance (revision 7) is superseded: revision 8 corrects the writer instead.
- **Revision 8 limits and open items:**
  - **Owner decision pending:** the 69 / 138 rows on faces EnergyPlus does not use keep both fixtures BLOCKED owner
    (exit 12): accept them, or an S07 3a scope decision (separate SAM PR). Neither is PASS until then; exit 12 is not
    a Phase B PASS, and no roadmap exception is requested.
  - **Behaviour change:** every SAM model whose first-layer materials have differing faces now exports `_Internal`
    variants with different zone-face absorptances. **The effect on results is unmeasured**; nothing was simulated.
  - A single-layer outdoor or interzone construction with differing faces now makes the conversion invalid
    (SAM-OS-MAT-003); such models stopped converting cleanly. The S09 test shows the writer still saves the OSM with
    that surface unconstructed; whether EnergyPlus accepts such a model is not checked (no engine run).
  - The missing-first-side error is verified at unit level only (no realistic model found that drops only that side).
  - An unstated Internal* takes the External* value on a zone face; since revision 9 this is reported
    (SAM-OS-MAT-004, Summary) and S09 reports those rows as S-UNSPEC EXPORT-DEFECT. Accepting the inheritance would
    need an S07 default for internal faces (an S07 decision), not a writer change.
  - **Round-trip layer order (pre-existing, found in revision 9):** the importer keeps OpenStudio's outside-first order,
    so a multi-layer construction comes back reversed. Separate task and decision.
  - Import merging uses only `SAM.MaterialGuid`; objects written before revision 9 lack `SAM.Written.*`, so an unstated
    value on them is kept as an edit of unknown provenance (reported).
  - Opaque door panes keep the external values on both faces (SAM-OS-MAT-002 information), outside this scope.
  - Import (revision 9): compatible face and thickness objects of one source Guid merge into one material; conflicting
    ones are imported separately with a warning, each with a new Guid except the identity carrier.
- **Still open:**
  - **OD-L1 (S07 §8):** suitability of p₀ outside suspended LED office lighting, and whether a writer may replace a
    template-prescribed value with it. The confirmed reading lets OpenStudio apply p₀ to an unstated p, stated and
    claiming nothing beyond the category. No current fixture uses the fallback.
  - The Tas-side lighting change needs a separate, OD-L1-gated SAM_Tas approval.
  - The lighting visible-fraction default of revisions 1–4 is withdrawn by L1; it needs no decision.
- **Compatibility risks (revision 5):**
  - Every OpenStudio export changes its `Lights` fractions. An authored p keeps its total radiant share, but F_V moves
    from the view coefficient (or 0.25) to p·s. An unstated p moves from 0.32 / 0.25 (sum 0.57, convective 0.43) to
    0.3345 / 0.2415 (0.576, 0.424). EnergyPlus results for lighting gains change; nothing was simulated here.
  - An authored view coefficient no longer reaches EnergyPlus.
  - Importing an OSM sets p = F_R + F_V (previously F_R) and no view coefficient. SAM files imported earlier hold the
    old meaning and cannot be identified or migrated (§5.1).
  - A new Summary information line per SpaceType (`SAM-OS-IC-002`) appears in every conversion with lights. A consumer
    counting Information lines or `SAM-OS-*` codes sees one more code; codes are added, none renamed.
  - The Tas side is unchanged until the separate, OD-L1-gated SAM_Tas PR: for an unstated p Tas keeps its TBD value
    (S07: EXPORT-DEFECT on the S08 side), so OpenStudio and Tas differ for unstated p until then.
- **Revision 6 limits:**
  - A plenum-calculated return air (`Yes`) is refused as unsupported, not verified. A dynamic read-back would need its
    own contract row.
  - The plenum setting and the domain guard read the IDF, which EnergyPlus runs. The OSM `OS:Lights:Definition` plenum
    field is not cross-checked against it, as no Lights field was before.
  - The domain boundary 1e-12 is a read-back constant documented in `OsQuantities`, not an S07 tolerance.
- **Not covered by revision 5:** the acceptance and packing scripts (not run; the committed pack is pre-L1); a
  `Lights` object written with a blank Fraction Radiant or Visible (the writer always states them; the comparator reads
  blank as 0); the LED view-coefficient value (unresolved in §5.1, not used by OpenStudio).
- **Translator version:** the IDF comes from the OpenStudio SDK 3.10.0 translator (it matches the SDK that wrote the
  OSM). A production run translates with the CLI. This workstation's CLI is ladybug 3.11.0, and S06 prefers an
  SDK-matching CLI. The CLI translation was not exercised.
- **Not exercised by the fixtures:** internal floors and ceilings, internal apertures, gas glazing, `Material:AirGap`,
  shading, design days (T-DD), leap years, weekday sub-profiles, multi-space zones, an override in a real model.
  - Rotated models (a stated SAM North Angle) are refused as unsupported, not compared. World coordinates are
    supported (absolute vertices, no zone origin or rotation), and only World with a non-zero north axis or zone
    relative north is refused. No compared model uses World; only that refusal is tested.
  - Single-layer opaque constructions are absent from the fixtures; revision 8 tests them on modified copies only.
  - The reader reports each of these as unreadable, unsupported or not applicable, never as a match.
- **S08 record discrepancy (record only; SAM_Tas not changed):** SAM_Tas
  `Documentation/S08-PRC-FixturesAcceptance-PR.md:78` lists the two-zone openings as "A framed south; B framed north,
  unframed east". The signed-off `s08-fix-two.json` (`20da9b58…`) contains one aperture (A, framed south), as its
  OSM/IDF do. The fixture bytes are unchanged; the S08 record text needs a separate correction.
- **Space-type reuse:** OpenStudio space types are shared per content hash, so the V1 note and the invalid-selection
  error appear once per SpaceType and once per override.
- **OSM counterparts, one direction only (revision 3):** each IDF zone and subsurface must have its one OSM object. An
  OSM `OS:ThermalZone` or `OS:SubSurface` with no IDF object of its name is not looked for by `Multiplicity`. Closing
  that would be a separate, owner-scoped check, not part of this fix.
- **The Build 39603021836 cause (revision 4):** likely id reuse, but unproven. The run kept no output, id reuse was not
  measured on a GitHub runner, and an exception inside `compare` is not excluded by CI evidence. A recurrence now
  uploads `SAM_OpenStudio-s09-diagnostics`, which names the branch.
- **S08 has the same id-only check (observation, SAM_Tas not changed):** SAM_Tas
  `SAM_Tas/SAM.Analytical.Tas.DirectT3D.Validation/ReadBack/ReadBackCompare.cs:159` (`c50e9169`). Whether id reuse can
  reach it was not assessed; any change there is a separate SAM_Tas task.
- **Duplicate `OS:Building` (observation, not changed):** `Orientation` reads it with `SingleOrDefault`. A copy of the
  box run with two `OS:Building` objects makes `compare` throw and exit 70 (infrastructure failure). It fails closed,
  but not as a named BLOCKED technical entry (`revision-3/README.md`).

## Next step

Both fixtures report BLOCKED owner, exit 12, with only unused-face rows left. Do not start S10.
- **Independent review of revision 9** (`354e5322fa3a4c31a984de911c48534e768c40e4` and the record head in the PR
  description): edit detection against the written values, the merge rules and their conflicts, SAM-OS-MAT-004 and the
  NaN-as-unstated reading, the regressions, controls and A/B. The review of revision 8 raised these three findings.
- The owner decides whether the pre-existing import layer-order defect becomes its own task.
- Then the owner decides the unused-face rows (accept, or an S07 3a scope decision through a separate SAM PR).
- Optional, if the owner wants a refreshed committed pack: an authorised acceptance run (`Run-S09-ReadBack.ps1`, both
  phases, then `Pack-S09-Evidence.ps1`) at the reviewed head. It exports and translates with the OpenStudio SDK only.
- Then the owner's merge decision, the known-SHA `PROJECT_PROGRESS.md` closeout and guarded branch cleanup.
- The roadmap S09 row, and any Phase B gate exception, change only through a separate SAM PR.
- The construction-performance route needs its own owner start and authorisation for any EnergyPlus run.
