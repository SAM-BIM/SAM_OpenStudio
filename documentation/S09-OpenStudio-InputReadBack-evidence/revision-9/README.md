# S09 revision 9 evidence: review of revision 8 (native edits, merging, inherited faces)

Revision 9 code: `354e5322fa3a4c31a984de911c48534e768c40e4`. It addresses the three findings of the independent review of
`901e592a`. The corrected exposed faces, the single-layer refusal, lighting and zero-delivery ventilation are unchanged.

| File | Content |
|---|---|
| `box-report-approved.txt`, `two-report-approved.txt` | the fixture reports of the revision-9 S09 run: byte-identical to `revision-8/` (BLOCKED owner, exit 12) |
| `box-unstated-light-absent-report.txt` | the box with Plasterboard's internal light reflectance removed: S-UNSPEC EXPORT-DEFECT on exactly the 5 Plasterboard zone-face light rows |
| `test-summaries.txt` | S09, writer/importer and benchmark summaries at `354e532`, and the A/B against `901e592` |

## Findings and results

1. **Native edits.** Each opaque object records what was written (`SAM.Written.*`). An absorptance that differs on import
   is an edit, kept on the face the object carries and reported (SAM-OSI-MAT-001). The review's reproduction (light
   reflectance unstated, visible absorptance 0.7 → 0.42) now imports internal light reflectance 0.58 and re-exports
   0.42; revision 8 lost it. An unchanged default stays unstated (control).
2. **Merging.** Face and thickness objects of one explicit source Guid become one SAM material, its Guid restored and
   each layer's thickness kept. Thermal, authored-optic or per-face conflicts are reported (SAM-OSI-ID-001) and not
   merged; objects without source identity are never merged by name.
3. **Inherited faces.** An unstated internal value (absent or NaN) on a zone face that takes the external value raises
   SAM-OS-MAT-004 at Summary; import keeps it unstated, and S09 keeps S-UNSPEC (EXPORT-DEFECT under S07).

Found, not fixed: the importer keeps OpenStudio's outside-first layer order, so multi-layer constructions come back
reversed on round trip (pre-existing; see the PR record).

## Reproduce

```text
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --filter 'TestCategory!=Simulation'
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --filter TestCategory!=Simulation
```
