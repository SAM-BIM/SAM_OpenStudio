# S09 revision 5 evidence: the S07 §5.1 lighting split (amendment L1)

Contract: SAM `documentation/SIMULATION_INPUT_MAPPING_S07.md` §5.1, merged by SAM#217 as `113873a0` (post-merge Test run
39667707673 and Build (Windows) run 39667707679 succeeded on `113873a0`). Revision 5 code: `20f8fb6`.

**Scope of this evidence.** These are the S09 test project's own fresh-process runs (export in-process, then `read` and
`compare` each in a new process), at `20f8fb6`, Release x64, on this workstation. The acceptance and packing scripts
(`Run-S09-ReadBack.ps1`, `Pack-S09-Evidence.ps1`) were **not** run under this revision's scope. The committed `runs/`,
`negatives/` and `summary-*.json` therefore stay the revision-3 `acc3` pack, which predates amendment L1.

| File | Content |
|---|---|
| `box-report-approved.txt`, `two-report-approved.txt` | `compare-approved/report.txt` of the two fixture runs of `ReadBackTests` at `20f8fb6` (harness `os-input-readback.dll` `3549e421…`, library `b4e03d5f…`) |
| `idf-diff-acc6.txt` | `diff` of each fixture IDF from the revision-4 acceptance (`C:\TasOut\s09\acc6`, machine-local) against the revision-5 test run, with hashes |
| `test-summaries.txt` | the local test summaries at `20f8fb6`, and the A/B with the previous writer and importer |

## Results

- **Fixtures (S08 signed-off bytes, Budapest EPW `472ffb4e…`):** both BLOCKED owner, exit 12, complete coverage.
  - `s08-fix-box`: 319/319 (was 316: the lighting rows are now 5 per space, were 2). Match 220, WithinTolerance 25,
    AcceptedTransformation 4, NotApplicable 1, MappingDifference 69.
  - `s08-fix-two`: 592/592 (was 586). Match 393, WithinTolerance 46, AcceptedTransformation 14 (6 ventilation, 8
    lighting), NotApplicable 1, MappingDifference 138.
  - The only non-PASS items are the OS-MAT-SINGLE-SIDED rows (69 / 138). The lighting DOCUMENTED-DEFAULT items are
    gone: the view-coefficient comparison is removed, and every lighting row of the authored p passes.
- **IDF bytes:** each fixture IDF differs from acc6 only in the `Lights` Fraction Radiant and Fraction Visible lines
  (box 0.37/0.25 → 0.214869791666667/0.155130208333333; two-zone A 0.34/0.25 → 0.197447916666667/0.142552083333333,
  B 0.41/0.25 → 0.238098958333333/0.171901041666667). Return Air Fraction stays blank.
- **A/B:** with the previous writer and importer (`6a36afb`) restored over `20f8fb6` and rebuilt, 18 writer tests and 21
  S09 tests fail (the S09 failures include every fixture-verdict test, because the old Fraction Visible 0.25 FAILs the
  lighting rows). The working tree was restored and rebuilt before the final runs.

## Reproduce

```text
dotnet build SAM_OpenStudio.sln -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --no-build --filter 'TestCategory!=Simulation'
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --no-build --filter TestCategory!=Simulation
```

The fixture reports are written under the S09 test output directory, `s09-runs/box-<id>/compare-approved/` and
`s09-runs/two-<id>/compare-approved/`. Prerequisites are as in the record (sibling `SAM` built, `SAM_Tas` at
`sow/2026-Q4`, the Budapest EPW from the SAM checkout).
