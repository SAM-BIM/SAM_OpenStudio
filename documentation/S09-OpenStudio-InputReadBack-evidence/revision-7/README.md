# S09 revision 7 evidence: the owner decisions and acceptance reporting

Revision 6 (`8fd0ffc2`) passed independent technical review. On 2026-10-22 the owner took three decisions: optics
option 1 (OS-MAT-SINGLE-SIDED accepted for the two reviewed fixtures only, effect on results unmeasured), ventilation A
(T-VENT-INACTIVE confirmed) and lighting B1 (the Decision 5 reading confirmed; OD-L1 stays open). Revision 7 code:
`614a7a8d70b6a70c5628a0ef3b57eaec0c5ec794`. It reports the optics acceptance; ventilation A and lighting B1 confirm existing behaviour.

| File | Content |
|---|---|
| `box-report-approved.txt`, `two-report-approved.txt` | the `report.txt` of each fixture from the revision-7 S09 test run |
| `test-summaries.txt` | the local S09 test summary at `614a7a8` and the A/B of the new and changed tests |

## Results

| Fixture | Revision 5/6 | Revision 7 |
|---|---|---|
| `s08-fix-box` | BLOCKED owner, exit 12, 319/319 | **NOT PASS, accepted limitation, exit 14**, 319/319, identical outcome counts |
| `s08-fix-two` | BLOCKED owner, exit 12, 592/592 | **NOT PASS, accepted limitation, exit 14**, 592/592, identical outcome counts |

- Against `revision-5/`, each report differs only in its status line and in the listed rows' reasons ("Accepted by the
  owner (reference); this is not a pass.").
- The acceptance (`Compare/OwnerDecisions.cs`) applies only under the approved rules, to a fixture id and frozen-model
  SHA-256 that are both a reviewed fixture's, and only to 3a MAPPING-DIFFERENCE rows. Anything else stays exit 12.
- A wrong opaque optical value still FAILs (exit 10) under the acceptance, on its external-face rows.

## Reproduce

```text
dotnet build benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64 --no-build
```

The acceptance and packing scripts were not run; the committed pack stays acc3. No engine was run (the tests export
and translate with the OpenStudio SDK only).
