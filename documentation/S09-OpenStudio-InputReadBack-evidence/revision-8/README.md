# S09 revision 8 evidence: opaque face-optics correction

The owner asked for the opaque-material mismatch to be investigated and corrected before any acceptance (2026-10-22).
Revision 8 code: `fc83a6df1f843f82a043353616a2b4e42a94e6d6`. Revision 7's optics acceptance is removed; its commits
and `revision-7/` evidence stay as history.

| File | Content |
|---|---|
| `box-report-approved.txt`, `two-report-approved.txt` | each fixture's `report.txt` from the revision-8 S09 test run |
| `idf-diff-rev7.txt` | the fixture IDFs of the revision-7 test runs against revision 8 (zero-context diff) |
| `test-summaries.txt` | S09, writer/importer and benchmark summaries at `fc83a6d`, and the two A/Bs |

## Results

| Fixture | Revision 7 | Revision 8 |
|---|---|---|
| `s08-fix-box` | NOT PASS, accepted limitation, exit 14 | **BLOCKED owner, exit 12**, 319/319; 69 MAPPING-DIFFERENCE rows, all on faces EnergyPlus does not use |
| `s08-fix-two` | NOT PASS, accepted limitation, exit 14 | **BLOCKED owner, exit 12**, 592/592; 138 MAPPING-DIFFERENCE rows, all on unused faces |

- Every used zone face holds the authored value: box 18 rows, two-zone 36 rows pass and are never listed.
- The IDFs change only in the `_Internal` materials (internal-face absorptances, e.g. Plasterboard 0.91 / 0.32 / 0.30
  instead of 0.90 / 0.38 / 0.36) and the construction layers that name them.
- Swapped-face regression: the external values written on each zone-face variant FAIL exactly 18 / 33 zone-face rows.

## Reproduce

```text
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --filter 'TestCategory!=Simulation'
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --filter TestCategory!=Simulation
```

Machine-local: the test runs under `bin/x64/Release/net8.0/s09-runs` (not committed). No engine was run; the tests
export and translate with the OpenStudio SDK only.
