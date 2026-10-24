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
- Tested content SHA: see "Tests and validation" (recorded after the code commit).
- Next: independent review of Revision 1; owner decisions on the two findings below before Revision 2.

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
- **Per file, per (zone x quantity) (C1/C1b):** SQL: exactly one Hourly dictionary entry; rows of the RunPeriod
  environment (type 3, named as the RunPeriod) with `Interval 60`, `Minute 0`, `Dst 0`, `WarmupFlag 0`, no repeated
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

## Findings for owner decision (not fixed: importer and SAM core are out of S11 scope)

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
- Owner options for each (not chosen here): fix in a separate importer/SAM PR before Revision 2; or accept and record the
  limitation and how the row is judged. Revision 2 should not start until both are decided.

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
- Not changed: importer, writer, SQL readers, schema, SAM_UI, SAM_Tas, SAM core, S10.

## Tests and validation (local, Release x64)

To be completed with the tested content SHA (see the record-only follow-up commit).

## Remaining limitations and risks

- No S11 run exists. The channel readers were exercised on synthetic files and, as a development probe only (not
  evidence; older content, one zone, no SAM model match), read-only on the S05 output
  `C:\TasOut\s05\runs\c2-openstudio-explicit\000000_…_275dbafb\run` (EnergyPlus `25.1.0-1c11a3d85f`, SQL `32b5dcc8…`,
  hashes unchanged by the read): calendar 8760 from `SAM_RunPeriod` 2006; one SQL environment and one ESO block; all six
  series complete in both files with units J/hr; SQL = ESO with relative difference 0 in every hour (the ESO prints full
  precision); timestep meters equal the zone Total within ~1e-15 relative. Real cooling latent there is ~9 % of Total
  (Total 16.40 GJ, Sensible 14.91 GJ); heating Total = Sensible, so heating F3 margins are 0 on such data (OD-7 concerns
  cooling). The full read/subject/compare chain has not seen a real run; Revision 2 may still BLOCK.
- F-S11-1 and F-S11-2 above will very likely make Revision 2 FAIL on unmet hours unless decided first.
- Scripts are parse-checked only; `Build-S11-Receipt.ps1` and the readback/negative phases have not been executed.
- The full-solution local build redirects `APPDATA` because the Grasshopper post-build deploys into `%APPDATA%\SAM`.

## Recommended next step

Independent review of Revision 1 (another model or session, High effort, new session, Plan mode, read-only), focused on
reference independence, the completeness logic and the two findings; then the owner decides F-S11-1/F-S11-2 and whether
to authorise Revision 2 (2 engine runs).
