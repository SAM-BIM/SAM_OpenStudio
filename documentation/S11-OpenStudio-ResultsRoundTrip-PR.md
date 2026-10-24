# S11: OpenStudio results round-trip validation

Roadmap row S11 (`SAM/documentation/SIMULATION_ROADMAP_Q4_2026.md`): SAM-imported results and B1a against an
independent SQL/ESO read; label total versus sensible cooling (F3).

> Phase B remains subject to S10's declared validation limitation (`SAM|FixB|Heating|designDayPeak.hourOfDay` BLOCKED);
> an S11 PASS is not a Phase B PASS.

## Status

- **Revision 1 (code + offline tests) only**, owner-authorised 2026-10-24 together with approval of the revised S11
  plan (local, non-Git: `SAM-BIM\S11_IMPLEMENTATION_PLAN.md`, plan revision 2). Draft PR into `sow/2026-Q4` from
  `feature/s11-openstudio-results-roundtrip`, base `ec204cbb`.
- Not authorised and not done: EnergyPlus/OpenStudio runs (plan section 9, Revision 2), merge, Ready, any other stage.
  No simulation, CLI engine run or Tas/COM operation was performed. No S11 acceptance claim exists yet.
- Independent review of Revision 1 (2026-10-24, reviewed head `06084afb`, tested `246c8fed`): **REVISE** with two
  findings, R1 (acceptance driver) and R2 (reporting frequencies / weather-run environments). Both are corrected in
  "Revision 1 correction" below, code and offline tests only, on this same Draft PR.
- The R1/R2 correction was independently accepted (2026-10-24, tested `059aed6f`, head `9ce146cd`).
- **Offline integration of the production fixes (2026-10-24, owner-authorised):** F-S11-1 (SAM_OpenStudio#35, merged
  `4b020585`) and F-S11-2 (SAM#219, merged `ac68ad0f`) are integrated; the tests that pinned both defects are replaced by
  positive regressions on the signed-off fixtures. Tested content `76d61612ebe477f57cb44517f82e5353b2abe3b0` (on Q4 merge
  `43e3541c3cdcc8b0a43f4c73f062db694aafbdf2`); see "Integration of F-S11-1 and F-S11-2".
  No engine run, Ready, merge, SAM_UI run, S12 or Phase B acceptance.
- Tested content SHA: see "Tests and validation" (recorded after each code commit, in a record-only commit).
- Next: independent review of the integrated head, then a separately authorised Revision 2 (real-run acceptance).

## Owner directions applied (2026-10-24)

1. Revision 1 only; plan saved as `S11_IMPLEMENTATION_PLAN.md` (verbatim, with the approval header).
2. Both new projects (`SAM.Analytical.OpenStudio.ResultsReadBack`, `SAM.Analytical.OpenStudio.ResultsReadBack.Cli`)
   are in `SAM_OpenStudio.sln` and in the CI build checks: a new `build.yml` step fails if either is missing from the
   solution or produced no output after the solution rebuild; the S11 tests run in the existing read-back test step
   (renamed "S09 native-input and S11 results read-back tests"). This is the only workflow change.
3. Duplicate room names are handled through geometry and identity, never rejected because names match: the reference
   identity is the S09 geometry bijection (names only ever contradict it). Tests: two spaces with the same name PASS
   (`S11IdentityTests.DuplicateSpaceNames_DistinctGeometry_ResolvedByGeometryAndPass`); with the same name AND the
   fixture's shared GUID prefix the identity still resolves both by geometry and only the production naming collision is
   reported (`..._SharedGuidPrefix_IdentityStillByGeometry_ProductionKeyCollisionReported`).

## Revision 1 correction (review findings R1, R2; 2026-10-24)

Scope as directed: code and offline tests only; no EnergyPlus run, acceptance run, merge, Ready or next stage; the
importer, SAM core and the fixtures are unchanged.

| Finding | Disposition |
|---|---|
| **R1** `Run-S11-Results.ps1` recorded comparator exits without enforcing them, could read an earlier report after an infrastructure error, and recorded unexpected negative outcomes and changed originals without failing (review probe: two comparator exits 70 + old PASS reports -> printed PASS, exit 0). | **Fixed.** Exit 70 STOPPED at once on any step that does not complete (exit other than 0/10/11, no exit code, cannot start), on an output a step did not write itself (each output's write time + hash is taken before and after the step, so no clock comparison), on a compare report whose verdict/ExitCode disagree with the exit or whose `Inputs` hashes are not the files of that directory, and on a changed original; a stop writes `summary-<phase>.json` with `result = STOPPED`, never an earlier verdict. Before and after each run (readback: after compare; negative: after every case) the model, `in.idf`, SQL, ESO, `b1a.json` and `sam.json` must equal the run-manifest hashes, the manifest paths must lie in the run directory, and the active receipt, harness and producer must equal the hashes the run recorded; the receipt must list the executables the driver starts. readback collects numerical FAIL/BLOCKED for both fixtures and exits 10 (any FAIL) / 11 (any BLOCKED, or OD-7 met by neither fixture) / 0. negative takes each case's required exit from the driver (negative.json must agree or the phase stops), checks that each copy differs from the original in exactly the files the case mutates, exits 10 on any case not as required and on a control that did not PASS; without a PASSing control no mutation outcome of that fixture is marked evidence. `-HarnessExe`/`-BenchmarkExe` added for the stand-in tests (must still be receipt files). |
| **R2** `SqlResultRead`/`EsoResultRead` dropped non-Hourly dictionary entries of the required variables (review copy `extra-frequency-two/`, an extra ESO `!TimeStep` Total Cooling entry + record `999999,0`: still PASS 85/85); the SQL reader accepted a second type-3 environment named otherwise (production reads every type-3 environment). | **Fixed.** Both readers judge every dictionary entry of a required variable (SQL name match now case-insensitive, as ESO): Hourly entries form the series; any other frequency for that key is an S11-C1 problem that blocks it, whatever its values. Meter frequency handling is unchanged (separate, optional). SQL: exactly one EnvironmentType 3 environment, named as the RunPeriod, else a global block. Rerun of the review copy with the rebuilt CLI: **BLOCKED** (68 PASS, 0 FAIL, 17 BLOCKED; exit 11), reason `unsupported reporting frequency 'TimeStep' (report id 999999 ...)`. |

Tests added: `S11DriverTests` (42 cases; Windows PowerShell 5.1 runs the real driver against stand-in run folders,
manifests and receipt, with `S11DriverStandIn.ps1` as the harness and an inert producer that is asserted never to
start): fully passing control (0); the review reproduction, both comparators 70 with old PASS reports (70, no `two`
step, no PASS printed); read 70, subject 64, subject/compare exit 0 without a fresh output, verdict/exit disagreement
both ways, report judging other files (each 70); numeric FAIL collected for both (10); BLOCKED (11); FAIL + BLOCKED (10);
OD-7 met by neither (11); 10 changed inputs/builds x 2 phases (idf, SQL, ESO, b1a, sam, recorded model hash, receipt of
another build, changed harness assembly, harness not in receipt, manifest path outside the run; each 70 before any step
of that run); an original changed by a readback step and by a negative case (70); negatives all as required (0);
mutation not detected (10, collected); failing control (10, no evidence); copy not mutated (10); negative.json
disagreeing with the driver (70); negative compare 70 (70); run phase without `-ConfirmEngineRuns` (70, nothing
created). `S11CompletenessTests` gains three BLOCKING defects: SQL extra `HVAC System Timestep` entry with a zero value,
ESO extra `!TimeStep` entry with a zero record (the review case), and a second SQL type-3 environment with another name
and rows in both files (paired production-reader check: it still returns values).

## Integration of F-S11-1 and F-S11-2 (offline, 2026-10-24)

Owner-authorised offline integration on this Draft PR. Write scope SAM_OpenStudio (this branch); SAM, SAM_Tas,
SAM_Validation and SAM_SQLite reference-only. No EnergyPlus/OpenStudio CLI simulation, Tas/COM operation, SAM_UI run,
Ready, merge, deployment, S12 or Phase B acceptance. Revit compatibility (SAM#219) stays deferred by the owner.

**Branch update.** `sow/2026-Q4` (`194ee1e6`, containing #35 merge `4b020585` and its closeout) merged into this branch
as `43e3541c` (merge commit, no conflicts, no history rewrite). The merge brings the importer fix
(`OpenStudioSimulationRunner.cs`), its tests (`tests/.../C5/ZoneResultIdentityTests.cs`), its record and the Q4
progress closeout; this PR's own diff is unchanged by it.

**Tests replaced** (tested content `76d6161`, test project only; no production, library, CLI, script, fixture or
workflow change):

| Former test (pinned a defect) | Replacement (positive regression) |
|---|---|
| `Finding_SharedGuidPrefix_ImporterLosesUnmetHoursOfOneZone_S11Fails` (expected FAIL on the unmet rows of FixB) | `SignedOffTwoZone_SharedGuidPrefix_EachSpaceKeepsItsOwnResults`: the **unchanged** signed-off `s08-fix-two` (FixA `5080c000-…c0a1`, FixB `5080c000-…c0b1`) with zone-specific fractional not-met time; verdict PASS with 0 FAIL/BLOCKED; native Space, Zone and Ideal Loads distinct (2 each); each space's production Ideal Loads/ThermalZone key = its own native key (6 identity rows PASS); reference, SAM-P, SAM-I and B1a unmet hours exact per space (zone 0 14.5/14.75 h, zone 1 7.25/5.25 h, so a swap FAILs); the 4 SAM-I annual results attached to the space they reference (both spaces); no "matched no SAM space" / "No simulation result found" diagnostic. |
| `Finding_FractionalNotMetTime_StoredAsWholeHours_S11Fails` (expected b1a heating 14) | `FractionalUnmetHours_SurviveSqlImport_SamSaveReload_AndB1a` (`s08-fix-box`): independent SQL/ESO reference 14.5 h heating / 14.75 h cooling; SAM-P, SAM-I and B1a rows PASS with reference = subject = 14.5 / 14.75 exactly; the producer's SAM objects after SQL import, the same objects after `sam.json` save and `OpenStudioBenchmarkInputs.Read`, the written `b1a.json` (`unmetHours.value`), and the SAM_UI/GH import (`Modify.AddResults`) saved as an `AnalyticalModel` SAM JSON and reopened, read through `TryGetValue<double>`: all 14.5 / 14.75. |

Other test changes: the re-GUIDed two-zone variant (`TwoDistinctPrefix`, space B -> `6181d111-…`) is retired; the
consistent two-zone CLI run, completeness, F3 and negative tests now use the signed-off fixture
(`S11Support.Two()`), so those offline proofs now also run on the shared GUID prefix. Synthetic fractional not-met time
is binary-exact quarter hours that differ per zone (was 0.5 h heating / 1/6 h cooling in every zone); the default
whole-hour data, every other series and all negatives are unchanged. The same-name tests in `S11IdentityTests` are kept:
same name + distinct prefix PASSes; same name + same prefix still reports the production key collision (the separately
tracked generated-name identity residual, left unmapped by #35 rather than guessed) and is now documented as such.

**Fail-before controls** (the tested test binaries with one dependency swapped for its pre-fix build; same filter):
- Pre-#35 importer (`SAM.Analytical.OpenStudio.dll` `b251fec0…`, built 2026-10-24 10:42 from this branch before the
  merge; has no `UniqueZoneIdentities`/`ReadZoneNames`): two-zone regression **FAILs** (FixA reads FixB's not-met time:
  delta 7.25 h heating, 9.5 h cooling, in SAM-P, SAM-I and B1a); the box regression passes (one zone).
- Pre-#219 SAM (`SAM.Analytical.dll` `1492f3ed…`, local `SAM\build` built 2026-10-23 from `b7c2c36a`): both
  regressions **FAIL** (half-to-even rounding: 14.5 -> 14, 14.75 -> 15; deltas 0.5 / 0.25 h).
- Baseline before the test change (merged code `43e3541c`, old tests): 240/242; exactly the two `Finding_` tests fail
  with "Expected FAIL, but was PASS", i.e. the integrated fixes remove both defects on the signed-off fixtures.

### Dependency provenance (local, isolated)

Isolated sibling workspace `SAM-BIM\.s11i\ws` (machine-local; short path for MAX_PATH): SAM exported with `git archive`
at merged Q4 `457defbb033fa4a58128f2395b87587f0210c608` (contains #219 merge `ac68ad0f…`) and built there
(`dotnet build SAM/SAM.Analytical/SAM.Analytical.csproj -c Release -p:APPDATA=<scratch>`; 0 errors); SAM_Tas source
export `922044fa9ca35062979689f4e359ea85bee577dd`; SAM_Validation `build/` from the local checkout at Q4
`2a3acfafb3919b4a0b81d67ef16a4b9d1e222f5d`; SAM_SQLite `build/` from the local checkout at `ec2eb86d00cac6e4cadf52b429f9d293eeabbd5e`
(both checkouts clean, equal to remote Q4); SAM_OpenStudio exported at `43e3541c` then the test edits (tree compared
equal to `76d6161`, excluding generated output). `APPDATA` redirected for every build; no Grasshopper project built, no
`%APPDATA%\SAM` deployment; the repository checkouts and their `build/` folders were not rebuilt or modified.

| Assembly (loaded by the test binaries) | SHA-256 |
|---|---|
| `SAM.Analytical.dll` (SAM `457defbb`; all 8 copies in the test/CLI outputs) | `21bec17f66e1c7cff13444ef29025adc96cab8a324237cd58fc2750f76e86536` |
| `SAM.Core.dll` | `43ad048540a48310598b4910ace9287b0dcbb5eb116533adc3124a5fd154c96b` |
| `SAM.Geometry.dll` | `f51b88d0691a4c160e059308096586aec618e8f4193ef550cc12715edf8274d5` |
| `SAM.Architectural.dll` | `fdacd08c21180048ff98b2596c0b8dbb48d36c225303332d8558b0df3d18c338` |
| `SAM.Weather.dll` | `02310d05ecd746c36f5fd660a50702645ebd4fc3a470f4935ecc9c703eca4fec` |
| `SAM.Math.dll` | `f33840c1fbf79f1bae1725eb6808a6344ad6fcbf463ae85139b7e0c53ef53ac4` |
| `SAM.Analytical.OpenStudio.dll` (importer incl. #35, built in the workspace) | `5d1e50df731b07ebc3a85eb4ded0a0ef6d24e1c536a5515fc28ccec561a49cfb` |
| `SAM.Analytical.Benchmark.dll` (SAM_Validation `2a3acfaf`) | `0dfdc8ec8f9b65f33d50a914ae4dd48c7d3be151abeb435e81a8ae4cee9d087b` |
| `SAM.Core.SQLite.dll` (SAM_SQLite `ec2eb86`) | `ba9400e9b9969ed8f16d3c1c4dac987bd4d70d9e067501546ba1cbe8d5a260c4` |

The SAM_Validation and SAM_SQLite hashes equal those recorded for SAM#219's validation. The SAM build is not
byte-identical to SAM#219's tested build (`eacd1ed8…`, other path); its behaviour is shown by the tests above.

### Validation (local, Release x64, tested content `76d6161`)

| Check | Command (from the isolated `SAM_OpenStudio`, `$A` = scratch APPDATA) | Result |
|---|---|---|
| Affected builds | `MSBuild.exe <project> -restore -t:Rebuild -p:Configuration=Release -p:Platform=x64 -p:UseSharedCompilation=false -p:APPDATA=$A -nr:false -v:m` for `InputReadBack.Tests` (S11 library, CLI, producer, S09 reader/CLI), `Benchmark.Tests` and `tests/SAM.Analytical.OpenStudio.Tests` | exit 0, 0 errors, 0 CS warnings (existing MSB3277 System.Memory only). Note: `-t:Restore;Rebuild` in one call failed on a fresh tree (CS0400 `global::OpenStudio`: restore output not visible to the same evaluation); `-restore` fixes it. |
| Read-back tests (S09 + S11) | `dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests -c Release -p:Platform=x64 --no-build` | **242 passed, 0 failed, 0 skipped**, 6 min 22 s (120 S09 + 122 S11; the two replacements counted in place of the two `Finding_` tests) |
| Producer tests | `dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests -c Release -p:Platform=x64 --no-build --filter "TestCategory!=Simulation"` | 58 passed, 0 failed, 0 skipped |
| Importer offline tests (incl. #35's 17 `ZoneResultIdentityTests`) | `dotnet test tests/SAM.Analytical.OpenStudio.Tests -c Release -p:Platform=x64 --no-build --filter "TestCategory!=Simulation"` | 488 passed, 0 failed, 2 skipped (the two real-SQL reproduction fixtures are not committed), 6 min 58 s |
| S09 saved-run replays (offline) | `os-input-readback compare --run C:/TasOut/s09/acc7-u1/<box\|two> --fixture-id s08-fix-<box\|two> --rule-set approved --out <scratch>`, on merged `43e3541c` and on `76d6161` | exit 0 all four; `report.txt` byte-identical to committed `acceptance-2026-10-23/runs/<box\|two>/report-approved.txt` (`868a5808…`, `9e3a3f0e…`) |
| Fail-before controls | the two regressions on copies of the tested binaries with the pre-fix DLL | as above: pre-#35 importer 1 FAIL / 1 PASS; pre-#219 SAM 2 FAIL |
| Fixtures | SHA-256 of `benchmark/SAM.Analytical.OpenStudio.InputReadBack/Fixtures/s08-fix-{box,two}.json` | `45524172…`, `20da9b58…` (unchanged, signed-off) |

Machine-local evidence (copy before switching machines): `SAM-BIM\.s11i\ev\` (`sources.txt`, `sam-build-hashes.sha256`,
`control-dll-hashes.sha256`, `logs\` with build logs, TRX and console logs, `s09-replay-base\`, `s09-replay-tested\`) and
the control binaries `SAM-BIM\.s11i\ctl\`. Not run: any `Simulation`-category test, `Run-S11-Results.ps1` run phase,
`Build-S11-Receipt.ps1`, SAM_UI, Tas. After the runs the isolated workspace holds no `eplusout.end`, `eplusout.err` or
`run.log` and no EnergyPlus/OpenStudio process was running; the `eplusout.sql/.eso` files under `s11-runs` are the
synthetic generator's.

**Separately tracked identity concerns (visible, not changed here; none blocks this integration):**
- `Modify.AddResults` `LookupSpace`/panel lookups resolve design-day (ZoneSizes) and surface results by the 8-character
  Guid suffix first. On `s08-fix-two` they would pick FixA for FixB's design-day or surface rows. S11 is not affected on
  these fixtures: the annual family references the space Guid directly (proved by the SAM-I attachment assertions),
  the fixtures have no design days (no ZoneSizes, OD-4 scope) and S11 compares no surface results. The same lookup
  feeds the annual "SQL zone '...' matched no SAM space" diagnostic, which therefore cannot fire for a FixB key whose
  prefix matches FixA (it never names a valid zone wrongly; it can only stay silent).
- Generated-name identity: two spaces with the same name AND the same Guid prefix get identical generated names;
  #35 leaves such keys unmapped and `ToSAM_SpaceSimulationResults` looks values up by the exact generated name. Not
  present on the signed-off fixtures; pinned by `S11IdentityTests.DuplicateSpaceNames_SharedGuidPrefix_...`.

## Work completed

| Plan item (section 6) | Implementation |
|---|---|
| 1. `ToBenchmark` split, zero rule, no-zone consumption, docs | `Modify.BenchmarkInputs(model, context)` returns `OpenStudioBenchmarkInputs` (model result, space results, design-load results, consumption-keyed flags); `ToBenchmark(model, context, inputs)` maps exactly those instances and reads only provenance from the context; the two-argument form calls both. `Peak()`: load exactly 0 = available 0, hour unavailable (space and model). Consumption with no zone key = unavailable. |
| 2. F3 note, `--sam-results` | `Modify.TotalBasisNote` added once to every Success document. `benchmark-openstudio --sam-results <json>` writes the same instances (envelope `s11-sam-results/1`, SAM JSON, UTF-8 no BOM, LF) after the B1a and prints their SHA-256; equal to `--out` is a usage error before any run. |
| 3. RB09 identity helper | `InputReadBack/Compare/NativeZoneIdentity.cs` (`SurfacesOf`, `MatchSpace`, `IdealLoadsOf`, `Resolve`); `OsComparison` now calls it. Behaviour-neutral: offline replay of the S09 `compare` on `C:\TasOut\s09\acc7-u1\{box,two}` gives `report.txt` byte-identical to the committed `acceptance-2026-10-23/runs/*/report-approved.txt` (`868a5808…` box, `9e3a3f0e…` two) before and after. |
| 4. `ResultsReadBack` library + CLI, scripts | Library (no production project, no SDK): `Native/RunCalendar.cs`, `HourlySeries.cs` (completeness), `SqlResultRead.cs`, `EsoResultRead.cs`, `Reference/ResultReference.cs`, `Compare/ResultCompare.cs`, `Report/ResultReport.cs`, `Classes/RunLayout.cs`, `SubjectDocument.cs`. CLI `os-results-readback`: `read`, `subject`, `compare`, `negative`. Scripts `Build-S11-Receipt.ps1`, `Run-S11-Results.ps1` (run phase refuses without `-ConfirmEngineRuns`), `Pack-S11-Evidence.ps1`; parse-checked under Windows PowerShell 5.1, not executed beyond the refusal guard. |
| 5. Tests in CI-run projects | `InputReadBack.Tests`: `S11Synthetic.cs` (real-schema SQL + ESO generator keyed by the S09 export IDF of a fixture), `S11Support.cs`, `S11ResultsTests`, `S11CompletenessTests`, `S11F3Tests`, `S11NegativeTests`, `S11IdentityTests` (+ unit tests), `S11SourceScanTests`. `Benchmark.Tests`: `S11ProducerTests`; golden updated by the note only. |
| 6. Record | This file. |

### Reference design as built

- **Calendar (C1):** from the single `RunPeriod` of the run's `in.idf`: must be 1/1-12/31 of a non-leap year (blank year
  allowed); 8760 keys (month, day, EnergyPlus hour 1-24). Anything else blocks every row.
- **Per file, per (zone x quantity) (C1/C1b):** every dictionary entry is judged: exactly one Hourly entry and no
  entry of another reporting frequency (both channels); SQL: rows of the RunPeriod environment (the only type-3
  environment, named as the RunPeriod) with `Interval 60`, `Minute 0`, `Dst 0`, `WarmupFlag 0`, no repeated
  `TimeIndex`; ESO: exactly one RunPeriod environment block, hourly stamps `StartMinute 0 / EndMinute 60`, DST 0, each id
  once per hourly block. The multiset of stamps must equal the calendar exactly (count alone never accepted). A series
  absent from either file blocks.
- **C3** units J/hr, finite, non-negative. **C4** SQL = ESO per hour, class E. **C5** geometry identity bijective, exactly
  one Ideal Loads per zone, every SQL/ESO key an identified Zone/Ideal Loads (unknown key blocks). **C6** Total >= Sensible
  each hour (else that load type's value rows block). **C7** each facility meter = sum of zone Total (R2) in each file
  where present (else informational); a disagreement blocks that consumption. **C8** ZoneSizes present iff
  `designDaySource != None`. **C9** EnergyPlus 25.1 in SQL `Simulations` and the ESO header.
- **Rows:** `reference`, `identity` (native, production Ideal Loads/Zone key = native key, B1a space set, provenance),
  `closure` (sam.json re-mapped by the producer = byte-identical B1a `model` and `spaces`; otherwise every SAM-P row
  blocks), SAM-P (model consumption R2, coincident peak R1, hour R0 earliest tie with SAM's hour 0 for an all-zero year,
  floor area/volume R1; per space x load type result, peak W R1, hour R0, unmet R2, design load), SAM-I (same space rows
  plus attachment; design-load value informational), B1a (as SAM-P with the zero rule, unmet, design load, Total-basis
  note). Verdict PASS only when every gated row PASSes; any FAIL gives FAIL (exit 10), otherwise BLOCKED (exit 11).
- **F3 margins:** every value row of a Total quantity reports `|ref(Total) - ref(Sensible)| / tolerance`; hour rows
  report whether the Sensible peak hour differs. `F3.MeetsOd7` (consumption and one space cooling peak >= 1000) is
  informational per run, for the OD-7 cross-fixture judgement in Revision 2.
- **Independence:** the library references only System.Data.SQLite, the S09 reader (`InputReadBack`) and SAM core/schema
  assemblies (asserted from the project file and the loaded assembly references); a source scan forbids the runner,
  importer/producer conversions, production naming/unit helpers, `AddResults` and the production namespaces in every
  library file, with a planted negative. Only `subject` (and the substitution negative) run production code.

## Findings F-S11-1 and F-S11-2 (RESOLVED by separate production PRs; integrated below)

Both were fixed outside this PR, each in its own reviewed and merged production PR, and are integrated here (section
"Integration of F-S11-1 and F-S11-2"). The text below is the original Revision 1 finding and proposal, kept as history.
F-S11-1 was fixed differently from the proposal: identity is proven unique by the SQL `Zones` inventory (see
`documentation/F-S11-1-Zone-Result-Identity-PR.md`). F-S11-2 was fixed as proposed (`UnmetHours` is a
`DoubleParameterValue(0, 8760)`; `OccupiedUnmetHours` unchanged).

- **F-S11-1 (importer, likely to FAIL on the real two-zone fixture):** both `s08-fix-two` spaces have GUIDs beginning
  `5080c000`. The importer remaps zone-level report keys (setpoint not-met time) onto Ideal Loads keys by the **last 8
  characters** (`OpenStudioSimulationRunner.NormalizeKey`), so one zone's unmet hours overwrite the other's and one space
  gets none. The geometry identity resolves both zones; S11 FAILs the unmet rows of the affected space in all three layers
  (`S11ResultsTests.Finding_SharedGuidPrefix_ImporterLosesUnmetHoursOfOneZone_S11Fails`). The positive two-zone tests use
  a variant with space B re-GUIDed (`6181d111-…`), recorded as such.
- **F-S11-2 (SAM core semantics, likely to FAIL on real runs):** `SpaceSimulationResultParameter.UnmetHours` is declared
  `IntegerParameterValue(0, 8760)` (SAM `SAM.Analytical/Enums/Parameter/SpaceSimulationResultParameter.cs:48`), so a
  fractional EnergyPlus not-met total (timestep fractions; the fixtures run 6 timesteps/hour) is stored as whole hours
  (14.5 h -> 14, 3.33 h -> 3). S11 compares the exact sum (R2) and FAILs
  (`Finding_FractionalNotMetTime_StoredAsWholeHours_S11Fails`). The default synthetic data use whole-hour not-met values
  so the other rows are exercised; no tolerance or semantic was changed.
- The independent review confirmed both as production defects, not engine differences. They stay visible FAILs on the
  signed-off fixtures: the fixtures, tolerances and reference values are unchanged, no row is informational, and the
  re-GUIDed synthetic variant remains unit-test coverage only, never acceptance evidence.

### Recommended bounded correction (historical proposal; superseded by SAM_OpenStudio#35 and SAM#219)

Recommendation: fix both before Revision 2 (real acceptance), each in its own PR, then rerun this PR's offline tests.

- **F-S11-1, SAM_OpenStudio importer only.** `OpenStudioName` (SAM.Core.OpenStudio `Query/OpenStudioName.cs`) is
  `SAM_<Type>_<SanitizedName>_<GuidFirst8>`; `NormalizeKey` (`OpenStudioSimulationRunner.cs:1033`) compares only the
  last 8 characters and returns the FIRST energy key that shares them. Smallest correction: compare the identity part
  after `SAM_<Type>_` (`<SanitizedName>_<GuidFirst8>`, case-insensitive), and leave a key unmapped when zero or more
  than one energy key match (no first-wins overwrite). This fixes `s08-fix-two` (FixA/FixB share `5080c000` but not
  names). It also corrects the other `NormalizeKeys` consumers that share the defect (internal gains, window solar,
  infiltration, temperature/RH series, `OpenStudioSimulationRunner.cs:968-981`). Compatibility: models whose 8-character
  prefixes are unique map exactly as now; colliding models change from silently wrong to correct. Residual (stated, not
  fixed): same sanitized name AND same GUID prefix stays ambiguous; it becomes visibly unmapped. Only full-GUID OpenStudio
  names would remove it, and that changes IDF names and the S09 evidence, so it needs a separate decision. Tests: the
  `Finding_SharedGuidPrefix_...` test should then PASS on the signed-off fixture, plus a runner unit test for ambiguity.
- **F-S11-2, SAM core (shared).** `SpaceSimulationResultParameter.UnmetHours` is `IntegerParameterValue(0, 8760)`
  (`SAM.Analytical/Enums/Parameter/SpaceSimulationResultParameter.cs:48`). `IntegerParameterValue` converts the
  importer's double to int, so 14.5 h is stored as 14 h; the B1a `unmetHours` is already a double `MetricValue`.
  Smallest correction: declare `UnmetHours` as `DoubleParameterValue(0, 8760)`, and consider `OccupiedUnmetHours` the
  same way for consistency. Compatibility checked by search (not by running): Tas writes integer counts
  (`SAM_Tas Query/UnmetHours.cs:136,155`), which stay exact as doubles; the Tas and OpenStudio B1a mappers already read
  `UnmetHours` as a double (`SAM_Tas .../ToBenchmark.cs:209`, this repo `ToBenchmark.cs:238`). No non-test
  `GetValue<int>` on the space parameter was found in SAM, SAM_Tas, SAM_UI or SAM_Validation (DesignExplorer reads the
  separate `AdjacencyClusterSimulationResultParameter.UnmetHours`, which is unchanged). Must be verified in that PR: saved
  JSON results holding integer `UnmetHours` still load and read (an int read as a double), Grasshopper/SAM_UI
  displays, the remaining SAM-BIM repositories, and S10's Tas results round-trip unchanged.
  Alternative with a smaller blast radius: a new double parameter written by the OpenStudio importer alongside the
  integer one. It is not recommended because SAM-I (the SAM_UI/Grasshopper layer) would keep showing whole hours.
- Revision 2 should not start until the owner decides both (fix as above, or accept and record a limitation and how the
  row is judged).

## Decisions and assumptions

- OD-2..OD-7 taken as proposed in the approved plan (Total basis + note, zero rule, missing = unavailable, earliest tie,
  engine-defined unmet hours, R0/R1/R2 + class E, ESO mandatory, completeness per file against the RunPeriod, design-load
  scope OD-4, OD-7 real-data requirement evaluated in Revision 2). OD-5 (SAM_Validation METRICS.md) is a later docs PR.
- The plan put the "production reader still returns values for the shared-error files" check in `Benchmark.Tests`; it is
  in `InputReadBack.Tests` (`S11CompletenessTests`), which references the importer too and owns the synthetic generator.
- The synthetic producer path calls the internal `OpenStudioSimulationRunner.ExtractLoads/ExtractResultSet` by reflection
  in tests only (as `benchmark-openstudio` does after a simulation); no `InternalsVisibleTo` or production change.
- SAM-I design loads come from the legacy design-day path (`Create.SpaceSimulationResults`); only their availability is
  gated, their value is informational (the `CalcDesLoad` mapping is gated through SAM-P/B1a, OD-4).
- `subject.json` for the Sensible-substitution negative is captured by the `negative` process and then mutated
  (recorded in `Mutation`); for the hour negatives read/subject/compare run fresh on the copy.

## Files changed

- `benchmark/SAM.Analytical.OpenStudio.Benchmark/`: `Classes/OpenStudioBenchmarkInputs.cs` (new), `Modify/ToBenchmark.cs`,
  `Program.cs`.
- `benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/`: `S11ProducerTests.cs` (new), `DiagnosticPropagationTests.cs`
  (the note), `Fixtures/golden-openstudio-benchmark.json` (one line: the note).
- `benchmark/SAM.Analytical.OpenStudio.InputReadBack/Compare/`: `NativeZoneIdentity.cs` (new), `OsComparison.cs`.
- `benchmark/SAM.Analytical.OpenStudio.ResultsReadBack/` (new project, 9 sources, 3 scripts) and
  `benchmark/SAM.Analytical.OpenStudio.ResultsReadBack.Cli/` (new project, 6 sources).
- `benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/`: csproj (references), 9 new `S11*.cs`.
- `SAM_OpenStudio.sln` (two projects), `.github/workflows/build.yml` (build check step, step label), this record.
- Revision 1 correction: `ResultsReadBack/Native/SqlResultRead.cs`, `Native/EsoResultRead.cs`, `Run-S11-Results.ps1`;
  tests `S11CompletenessTests.cs` (3 defects), new `S11DriverTests.cs` and `S11DriverStandIn.ps1` (copied to the test
  output by the test csproj).
- Integration (2026-10-24): merge of `sow/2026-Q4` (`43e3541c`; brings #35's runner change, tests and record, already
  on the base); tests `S11ResultsTests.cs` (two replacements), `S11Support.cs` (`Two()`, `SpaceAGuid`; re-GUIDed variant
  removed), `S11Synthetic.cs` (fractional not-met profile), `S11CompletenessTests.cs`, `S11F3Tests.cs`,
  `S11NegativeTests.cs` (signed-off fixture), `S11IdentityTests.cs` (comments); this record.
- Not changed by this PR: importer, writer, SQL readers, schema, SAM_UI, SAM_Tas, SAM core, S10, fixtures, tolerances,
  metric definitions, comparison gates, scripts, workflow.

## Tests and validation (local, Release x64)

Tested content: `246c8fedd1dcf45618df9241af682feadc1fa9bc` (code, tests, scripts, solution, workflow; this record section
was added afterwards in a record-only commit). Dependencies as built locally: SAM `b7c2c36a` (`SAM\build`), SAM_Validation
`2a3acfaf` (`SAM_Validation\build` schema), SAM_Tas `922044fa` (InputEquivalence + linked ReadBackExpected), SAM_SQLite
`ec2eb86`, all on `sow/2026-Q4`. Machine: Windows 11, VS 18 MSBuild, .NET SDK 10.0.401 targeting net8.0.

| Check | Command (from `SAM_OpenStudio`) | Result |
|---|---|---|
| Full solution, as CI | `MSBuild.exe SAM_OpenStudio.sln -t:Restore;Rebuild -p:Configuration=Release -p:Platform=x64 -p:UseSharedCompilation=false "-p:APPDATA=<scratch>" -m -nr:false -v:m` | exit 0, 0 errors; no compiler warning from a new or changed project (existing MSB3277 System.Memory warnings only). `APPDATA` redirected so the Grasshopper post-build did not deploy into `%APPDATA%\SAM` (verified unchanged). |
| New CI build-check logic, run locally | the `Check S11 results read-back projects are built` step body | both projects listed in the solution; both outputs present |
| Producer tests | `dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests -c Release -p:Platform=x64 --no-build --filter "TestCategory!=Simulation"` | 58 passed, 0 failed (51 existing + 7 `S11ProducerTests`) |
| Read-back tests (S09 + S11) | `dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests -c Release -p:Platform=x64 --no-build` | 197 passed, 0 failed, 0 skipped, 5 min 9 s (120 existing S09 + 77 S11) |
| S09 behaviour-neutral replay (offline, saved runs) | `os-input-readback compare --run C:\TasOut\s09\acc7-u1\<box\|two> --fixture-id s08-fix-<box\|two> --rule-set approved --out <scratch>` before and after the extraction | exit 0 both; `report.txt` byte-identical to committed `acceptance-2026-10-23/runs/<box\|two>/report-approved.txt` (`868a5808…b959fe`, `9e3a3f0e…a6c466d`) |
| Scripts | Windows PowerShell 5.1 `Parser.ParseFile` on the three `.ps1`; `Run-S11-Results.ps1 -Phase run` without `-ConfirmEngineRuns` | 0 parse errors, ASCII only; the run phase stops with exit 70 before creating anything |

The S11 tests include: the consistent two-zone run PASSing with `read`, `subject`, `compare` in three processes and the
four CLI negatives on copies (control 0, deleted hour 11, duplicated hour 11, Sensible substitution 10, original
unchanged); 22 completeness defects each BLOCKING (shared, one-sided, count-preserving shift, absent series/zone, repeated
ESO id, second dictionary entry, extra hour, Feb 29, warmup rows, merged design-day rows, non-annual/leap RunPeriod,
second ESO RunPeriod block, DST stamp) with the paired production-reader check for the shared ones; the F3 precondition
(every targeted row >= 10^6 tolerances, other peak hour) and 8 substitutions (consumption, space peak, model peak, all;
heating and cooling) each FAILing with no BLOCKED row; zero rule, ties, value slips, C3-C9, closure, identity and source
scan. Hosted CI at the earlier record head `06084afb`: Build `40067946762` and SPDX `40067946736` SUCCESS (the current
head and its checks are in the PR description).

### Revision 1 correction (local, Release x64)

Tested content: `059aed6f7efe2a43291e8b6433528c5819b8707f` (code, tests and stand-in; this record section was added
in a following record-only commit). Same dependency builds and machine as above. Only the affected projects were
rebuilt; no Grasshopper project was built; `%APPDATA%\SAM` has no file written after the build (its newest files date
from 07:00 that day, before this work).

| Check | Command (from `SAM_OpenStudio`) | Result |
|---|---|---|
| Affected projects | `MSBuild.exe benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -t:Rebuild -p:Configuration=Release -p:Platform=x64 -p:UseSharedCompilation=false "-p:APPDATA=<scratch>" -nr:false -v:m` (rebuilds the S11 library, CLI, producer, S09 reader/CLI and their references) | exit 0, 0 errors, 0 CS warnings (existing MSB3277 only) |
| Read-back tests (S09 + S11) | `dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests -c Release -p:Platform=x64 --no-build` | **242 passed, 0 failed, 0 skipped**, 7 min 13 s (120 S09 + 122 S11: 77 earlier + 42 `S11DriverTests` + 3 completeness defects) |
| Producer tests | `dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests -c Release -p:Platform=x64 --no-build --filter "TestCategory!=Simulation"` | 58 passed, 0 failed (existing test output; producer source unchanged by the correction) |
| S09 saved-run replays (offline) | `os-input-readback compare --run C:/TasOut/s09/acc7-u1/<box\|two> --fixture-id s08-fix-<box\|two> --rule-set approved --out <scratch>` | exit 0 both; `report.txt` byte-identical to committed `acceptance-2026-10-23/runs/<box\|two>/report-approved.txt` (`868a5808…`, `9e3a3f0e…`) |
| Review reproduction R2 | `os-results-readback read/subject/compare` on a scratch copy of the review's `extra-frequency-two/` (machine-local, parent workspace `.review-s11-r1-20261024`), model `s11-models/two-distinct-prefix.json` | BLOCKED 68 PASS / 0 FAIL / 17 BLOCKED, exit 11 (was PASS 85/85) |
| Scripts | Windows PowerShell 5.1.26100 `Parser.ParseFile` on `Run-S11-Results.ps1` and `S11DriverStandIn.ps1` | 0 parse errors, ASCII only |

The review's original `probe-driver.ps1` is superseded by `S11DriverTests.Readback_InfrastructureErrorWithOldPassReport_StopsAt70AndReadsNoReport`
(same scenario through the new `-HarnessExe` parameter; the old probe rewrote variable lines that no longer exist).
No EnergyPlus or OpenStudio run, no acceptance phase and no Grasshopper deployment took place.

## Remaining limitations and risks

- No S11 run exists. The channel readers were exercised on synthetic files and, as a development probe only (not
  evidence; older content, one zone, no SAM model match), read-only on the S05 output
  `C:\TasOut\s05\runs\c2-openstudio-explicit\000000_…_275dbafb\run` (EnergyPlus `25.1.0-1c11a3d85f`, SQL `32b5dcc8…`,
  hashes unchanged by the read): calendar 8760 from `SAM_RunPeriod` 2006; one SQL environment and one ESO block; all six
  series complete in both files with units J/hr; SQL = ESO with relative difference 0 in every hour (the ESO prints full
  precision); timestep meters equal the zone Total within ~1e-15 relative. Real cooling latent there is ~9 % of Total
  (Total 16.40 GJ, Sensible 14.91 GJ); heating Total = Sensible, so heating F3 margins are 0 on such data (OD-7 concerns
  cooling). The full read/subject/compare chain has not seen a real run; Revision 2 may still BLOCK.
- F-S11-1 and F-S11-2 are fixed and integrated (offline proof only). Real EnergyPlus output has not been read by the
  integrated chain; whether real not-met totals are fractional on these fixtures is unknown until Revision 2.
- Separately tracked, not blocking on these fixtures (see the integration section): AddResults suffix-only
  design-day/surface lookups and the generated-name identity residual. SAM#219's Revit compatibility is deferred by the
  owner (SAM_Revit is not touched). S10's declared heating design-day hour limitation is unchanged.
- `Build-S11-Receipt.ps1` and the run phase have not been executed. The readback/negative driver logic is now tested
  offline against stand-ins (`S11DriverTests`), not against a real receipt or real run folders.
- The full-solution local build redirects `APPDATA` because the Grasshopper post-build deploys into `%APPDATA%\SAM`.

## Recommended next step

Independent review of the integrated head (another model or session, High effort, new session, Plan mode, read-only),
focused on the two replacement regressions, the retirement of the re-GUIDed variant and the dependency provenance; it
should rebuild against merged SAM Q4 itself. After acceptance the owner decides whether to authorise Revision 2: exactly
2 `benchmark-openstudio` runs on the signed-off fixtures with the portable OpenStudio 3.10.0 (plan section 9), then
read/subject/compare/negative and an independent evidence review. Recommended executor: Claude Code / Claude Opus 5.5 /
High / new session / Build (fail-closed engine evidence). Not authorised by this record.
