# S09 revision 12 evidence: S07 §5.2 opaque optical face scope (amendment U1)

S07 amendment U1 was approved by the merge of SAM#218 (`b06dbe8c6341a810af7bef7b08570a0ddad0c31c`; closeout
`0168d03f7e9e56775868aecaca73d8224a2034c5`). Revision 12 implements it in the S09 comparator and report only. Tested
content: `8edf793a63b274cafe5e55b9e85fa597d6307a10`. The writer, importer, CLI, the shared core (SAM_Tas `c50e9169`) and the fixtures are unchanged.

| File | Content |
|---|---|
| `box-report-approved.txt`, `two-report-approved.txt` | each fixture's `report.txt` from the revision-12 S09 test session (byte-identical in two sessions) |
| `ab-pre-u1.txt` | the pre-U1 comparator (`adcad2f` library) on the same runs, against the U1 reports, with the method |
| `test-summaries.txt` | the S09, writer/importer and benchmark summaries and the DLL hashes |

## Results

| Fixture | Revision 11 | Revision 12 |
|---|---|---|
| `s08-fix-box` | BLOCKED owner, exit 12, 319/319; 69 MAPPING-DIFFERENCE | **PASS, exit 0**, 319/319; OUT-OF-ENGINE-SCOPE 105 (69 differing, 36 equal) |
| `s08-fix-two` | BLOCKED owner, exit 12, 592/592; 138 MAPPING-DIFFERENCE | **PASS, exit 0**, 592/592; OUT-OF-ENGINE-SCOPE 216 (138 differing, 78 equal) |

- PASS here is scoped native-input conformance of the OpenStudio export to S07. It is not simulation equivalence, not a
  Phase B PASS and not an owner acceptance of anything else.
- The differing rows are exactly the former MAPPING-DIFFERENCE rows. The equal rows are unused faces whose values agree;
  before U1 they were MATCH or WITHIN-TOLERANCE.
- `report.txt` SHA-256: box `868a5808d4cc1a96513e54b8189e647c8f45f4375b058a940ac1dee719b959fe`, two-zone
  `9e3a3f0ed0e2fd6d8703154a0cb71a69ffa10344d1e4e253c29886840a6c466d`.

## Reproduce

Build each project Release x64, then test without building:

```text
dotnet build benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --no-build --filter 'TestCategory!=Simulation'
dotnet build benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --no-build --filter 'TestCategory!=Simulation'
```

Prerequisites (sibling checkouts, as in CI): SAM `sow/2026-Q4` built into `SAM/build`, SAM_Tas `sow/2026-Q4`
(`c50e9169`), and the SAM#212 Budapest EPW in the SAM checkout. Machine-local: the runs under
`benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/bin/x64/Release/net8.0/s09-runs` (not committed). No engine,
simulation, sizing, acceptance or packing script was run; the tests export and translate with the OpenStudio SDK only.
