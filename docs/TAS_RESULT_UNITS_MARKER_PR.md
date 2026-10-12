# Result units marker on OpenStudio results, option A of SAM_Tas#92 (SAM_OpenStudio)

Branch `fix/tas-result-units-kwh` → base `sow/2026-Q4` (cut from `ffb97b7`). Record date: 2026-10-12. Part of a coordinated
set of four PRs; the design, compatibility table and merge order are recorded in SAM's
`documentation/TasResultUnits-PR.md` (record of record). This PR merges **after SAM**, before or after
SAM_Tas_Grasshopper, and is independent of SAM_Tas.

## Current status

PR open, **not merged**. Depends on the SAM PR (`fix/tas-result-units-kwh`: `ResultUnitsVersion`,
`Modify.SetResultUnitsVersion`); its CI builds SAM from the same-named branch until that merges. Awaiting final-head CI
and **owner review**.

## Work completed

- `Convert/ToSAM/SimulationResults.cs`: the model result, already kWh/kW, is now stamped `ResultUnitsVersion = 1`, so
  SAM's reader never has to infer its units from the producer. Values unchanged.
- `docs/SAM_OPENSTUDIO_RESULT_MAPPING.md`: the marker row.
- An older OpenStudio result without the marker is still read as kWh/kW by SAM (exact `Source` =
  `SAM.Analytical.OpenStudio`, this assembly's name, which a new test pins).
- Not changed: the OpenStudio benchmark `ToBenchmark` reads kWh/kW as stored (no conversion to remove), the coverage
  manifest (`AnalyticalModelSimulationResultParameter` is not a covered enum), SAM_Validation.

## Owner decisions

As in SAM's record. Specific here: OpenStudio stamps the marker too (recommended in the hand-over), so only legacy results
depend on the producer rule.

## Files changed

- `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToSAM/SimulationResults.cs`.
- `benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/ModelResultUnitsTests.cs` (new; in the benchmark test project
  because that is the offline project CI runs).
- `docs/SAM_OPENSTUDIO_RESULT_MAPPING.md`, `docs/TAS_RESULT_UNITS_MARKER_PR.md` (this record).

## Validation

- Benchmark tests (CI filter `TestCategory!=Simulation`) **42/42** (2 new: marked and read as stored; `Source` = assembly
  name = SAM's constant, and the same result without the marker reads identically). Test bin `SAM.Analytical.dll`
  SHA-256 `6e5658aa…` (the SAM branch build).
- Mutation: 1/1 killed (marker not stamped).
- Main offline suite `tests/SAM.Analytical.OpenStudio.Tests` (`TestCategory!=Simulation`): 327 passed, 2 existing SQL-fixture
  skips, 1 failure `OpenStudioCli_Discovered_And_OpensGeneratedOsm` because the OpenStudio CLI is not on this laptop's PATH;
  it passes with the verified portable OpenStudio 3.10.0 CLI on a process-local PATH (as recorded for PR25). Not run in CI.
- PR CI (`build`, `spdx`): see the PR.

## Unresolved issues and risks

None specific; order constraints are in SAM's record.

## Next step

Owner review. After SAM merges: rerun this PR's CI against merged Q4 and merge; closeout on `sow/2026-Q4` (docs-only,
`[skip ci]`).
