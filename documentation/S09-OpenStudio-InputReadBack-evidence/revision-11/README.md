# S09 revision 11 evidence: opaque multi-layer layer order on import

Revision 11 code: see `test-summaries.txt` (the tested content SHA). It implements the owner-approved opaque-only
correction proposed in revision 10. Importer only; the writer, S09, revision 10, lighting, ventilation and the
single-layer safeguards are unchanged.

| File | Content |
|---|---|
| `test-summaries.txt` | S09, writer/importer and benchmark summaries at the tested content, and the two A/B runs |
| `probe-output.txt` | the reviewer's revision-9 probe (unchanged, machine-local) run against the revision-11 binaries |

## What changed

1. **Layer order.** An opaque layered construction used by planar surfaces is read innermost layer first
   (`ToSAM/Construction.cs`). Fenestration (glazing, gas, SAM door panes), subsurface-only (unstamped door panes),
   shading-only, unused, air-boundary and non-layered constructions keep OpenStudio's order. A construction shared by
   surfaces and a subsurface or shade is split and reported (SAM-OSI-APX-001).
2. **Orientation.** An interzone panel is built from the side carrying the SAM-written forward construction (also for
   a geometric-fallback pair), and the spaces are added in a stable topological order that puts that side first,
   because the writer reads a panel's first space from the cluster's space order. This is a deviation from the
   proposal, which relied on the visit order; the geometric-fallback test with space B first failed without it. A
   panel whose building side is then second is re-read from its partner (APX-001).
3. **Fail closed** (owner instruction after the first draft reported the ambiguous re-read as a warning only): every
   imported opaque surface panel must reproduce its native layer order and the absorptances of every face EnergyPlus
   uses; otherwise error SAM-OSI-CON-002 and no model (`Successful` false). Edited/mixed models whose orientation or face
   roles would move a differing face, and contradictory forward sides, now fail named; equivalent cases succeed.

## Results

- The reviewer's multi-layer reproduction: re-export Brick → Insulation → Plasterboard_Internal, Plasterboard zone face
  0.42 (`probe-output.txt`, `ROUNDTRIP` lines; revision 10 re-exported Plasterboard → Insulation → Brick_Internal, 0.7).
- 30 new cases (`M4/OpaqueLayerOrderImportTests.cs`) compare every surface's layer thicknesses and thermal properties and
  both used faces' absorptances before and after export → import → export, or require the named CON-002 failure.
- Compatibility: models imported before revision 11 keep reversed constructions and are not migrated
  (`docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md`).

## Reproduce

```text
dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64 --no-build --filter 'TestCategory!=Simulation'
dotnet build benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.InputReadBack.Tests/SAM.Analytical.OpenStudio.InputReadBack.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet build benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64 --no-build --filter TestCategory!=Simulation
```

The A/B restores the listed files with `git show <commit>:<path> > <path>`, rebuilds, runs
`--filter "FullyQualifiedName~OpaqueLayerOrderImportTests"`, then restores the working files and rebuilds.
