# S09 revision 10 evidence: review of revision 9 (group membership, shared equal-face edits)

Revision 10 code: `78f10146481dbd4fe46af8761813fa4f9d58d357`. It addresses the two P1 importer findings of the independent
review of `806d5449` (code `354e5322`). The writer, the single-layer differing-face refusal (SAM-OS-MAT-003), S09,
lighting, ventilation and glazing are unchanged. The pre-existing multi-layer layer-order defect is unchanged and
still reproduces (see `probe-output.txt` and the proposal in the PR record).

| File | Content |
|---|---|
| `test-summaries.txt` | S09, writer/importer and benchmark summaries at `78f1014`, and the A/B against `806d544` |
| `probe-output.txt` | the reviewer's revision-9 probe (`Program.cs` unchanged, copied outside the repository) run against the revision-10 binaries |

## Findings and results

1. **A malformed member was aliased to a valid group.** `Convert.ToSAM(Material)` now validates the object itself
   (`ReadFaceObject`) before it looks up its source group, and returns the merged material only to a recorded member
   (`OpenStudioImportContext.OpaqueMaterialGroups` keeps the validated members' handles). The reviewer's third object
   (face `BROKEN`, conductivity 9) is now imported as its own material with conductivity 9, density 1000 and specific
   heat 1200, and SAM-OSI-ID-001 names it; the valid Plasterboard pair still merges (conductivity 0.25). The same holds
   for an identity that disagrees, a stamped optic that is not a fraction and a written record that disagrees with its
   authored face, in both import orders. A well-formed third object with other thermal properties stops the merge of
   the whole group (one conflict warning; each keeps its own properties).
2. **A shared equal-face edit was kept on one face only.** An edit is now kept on every SAM face the object supplies:
   an internal variant the zone face; an external object of a material whose authored faces differ the external face;
   an external object of a material whose faces agree the faces its construction positions expose (forward SAM
   construction: last layer zone face, first layer outer face; the unstamped reverse mirrors it; other constructions'
   exposed positions both faces; no exposed position: the external face and each stated internal one). The reviewer's
   single-layer reproduction now imports 0.58 on both faces and re-exports valid with visible absorptance 0.42; no
   SAM-OS-MAT-003. Genuine asymmetry (distinct variants) is kept.

## Reproduce

```text
dotnet build benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64
dotnet build benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --no-build --filter 'TestCategory!=Simulation'
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --no-build --filter TestCategory!=Simulation
```

The probe needs the reviewer's `Probe.csproj`/`Program.cs` (machine-local, outside this repository; it references
`tests/SAM.Analytical.OpenStudio.Tests/bin/x64/Release/net8.0/SAM.*.dll` by absolute path and the cached OpenStudio
3.10.0 and NUnit packages): `dotnet run --project Probe.csproj -c Release -p:Platform=x64`.
