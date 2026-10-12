# Result units marker on OpenStudio results, option A of SAM_Tas#92 (SAM_OpenStudio)

Branch `fix/tas-result-units-kwh` → base `sow/2026-Q4` (cut from `ffb97b7`). Record date: 2026-10-12. Part of a coordinated
set of four PRs; the design, compatibility table and merge order are recorded in SAM's
`documentation/TasResultUnits-PR.md` (record of record). This PR merges **after SAM**, before or after
SAM_Tas_Grasshopper, and is independent of SAM_Tas.

## Current status

PRs of the set: SAM-BIM/SAM#192 (1, marker and query), SAM-BIM/SAM_Tas_Grasshopper#13 (2), SAM-BIM/SAM_OpenStudio#26 (2), SAM-BIM/SAM_Tas#98 (3, last).

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

## Tested content, local versus GitHub checks, reproduction

- PR SAM-BIM/SAM_OpenStudio#26, base `sow/2026-Q4`. **Validated content SHA `693954d00cb33d890d0c00f6fb2c42af184b0a0c`**, built
  against SAM `b28daa77`; later commits are record-only. The final PR head and its GitHub checks are named in the PR
  description.
- "Validation" above is **local** unless it says "PR CI"; the GitHub checks are `build` (which runs the benchmark tests,
  `TestCategory!=Simulation`) and `spdx`. The main `tests/` project is not run in CI.
- Reproduce (SAM, SAM_SQLite and SAM_Validation built first):

  ```
  dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --filter "TestCategory!=Simulation"   # 42/42
  dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --filter "TestCategory!=Simulation"                     # 327 + 2 skips; CLI test needs the CLI on PATH
  ```

- Machine-local prerequisite: the portable OpenStudio 3.10.0 CLI (here `SAM-BIM\.icon-migration\runtime\OpenStudio-3.10.0+86d7e215a1-Windows\bin`) on a process-local PATH for `OpenStudioCli_Discovered_And_OpensGeneratedOsm`.

## Unresolved issues and risks

None specific; order constraints are in SAM's record.

## Next step

Owner review. After SAM merges: rerun this PR's CI against merged Q4 and merge; closeout on `sow/2026-Q4` (docs-only,
`[skip ci]`).
