# S09 revision 6 evidence: two fail-closed gaps in the lighting read-back

The independent review of `31af6022` reproduced two gaps in `OsComparison.LightingSplit`. Revision 6 code: `225881f0`.
The writer and importer split of revision 5 is unchanged.

1. **Native domain and energy conservation were not checked.** A split shifted by 0.0004 per field is within the 0.001
   tolerance of every row, so the reviewed head accepted p = 1 with Radiant + Visible = 1.0008, and p = 0 with Fraction
   Radiant −0.0004 (exit 12).
2. **The plenum-calculated return-air setting was not read.** With `Return Air Fraction Calculated from Plenum
   Temperature = Yes`, the reviewed head accepted the static split (exit 12).

| File | Content |
|---|---|
| `review-artifacts-ab.txt` | the reviewer's five machine-local artifacts (not committed), copied and read and compared fresh with the revision-6 harness, against the reviewer's reviewed-head results |
| `review-*-report.txt` | the revision-6 `report.txt` of each copied artifact |
| `test-summaries.txt` | the local test summaries at `225881f0` and the A/B of the 8 new tests |

## Results

| Reviewer artifact | Reviewed head `31af6022` | Revision 6 `225881f0` |
|---|---|---|
| control (p 0.37) | exit 12 | exit 12, identical outcome counts |
| p1-control (authored p = 1) | exit 12 | exit 12, identical outcome counts |
| p1-invalid-sum (sum 1.0008) | exit 12, lighting accepted | **exit 10**, 5 EXPORT-DEFECT: "Return Air + Radiant + Visible = 1.0008 exceeds 1" |
| p0-negative-fraction (Radiant −0.0004) | exit 12, lighting accepted | **exit 10**, 5 EXPORT-DEFECT: "Fraction Radiant -0.0004 is outside [0, 1]" |
| dynamic-plenum (Yes) | exit 12, lighting accepted | **exit 11**, 5 UNVERIFIED (unreadable) and a named `Unsupported` entry |

- **A/B of the new S09 tests:** with the reviewed `OsComparison.cs` restored over `225881f0`, the 5 regressions fail
  (exit 12 where 10 or 11 is required). The 3 valid controls (p = 1, p = 0, explicit No) pass under both.
- **Fixtures:** both `report.txt` files are byte-identical to `revision-5/`, and the IDFs keep their revision-5 hashes.

## Floating-point boundary

The domain guard accepts a fraction in [−1e-12, 1 + 1e-12] and a sum of at most 1 + 1e-12
(`OsQuantities.LightingFractionBoundary`).
- This absorbs only the rounding of the decimal IDF text to double, which is under 1e-15 for three values in [0, 1].
  The writer's p = 1 split (0.580729166666667 + 0.419270833333333) passes.
- It is not an S07 tolerance. It is applied before, never instead of, the S07 comparison, and no S07 tolerance changed.
- A violation is decided without a numeric comparison: the rows carry no comparison, and the reason names the
  violation.

## Reproduce

```text
dotnet build SAM_OpenStudio.sln -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64 --no-build
os-input-readback negative --run <a p = 1 export> --case neg-lighting-sum-above-one --out <new dir>
os-input-readback read --run <new dir>
os-input-readback compare --run <new dir> --fixture-id s08-fix-box --rule-set approved --out <new dir>/compare-approved
```

The other CLI cases are `neg-lighting-radiant-to-visible` (on a p = 0 export), `neg-lighting-plenum-yes`,
`neg-lighting-plenum-unknown`, `neg-lighting-plenum-relabelled` and the control `ctl-lighting-plenum-no` (on any export).
They are not in `Run-S09-ReadBack.ps1 -Phase acceptance-negative`, which requires exit 10 on the unchanged fixture
runs: the plenum cases exit 11, and the two partition shifts are invalid only on p = 1 or p = 0 exports. That script was
not run.
