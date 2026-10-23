# S09 revision 2 evidence: multiplicity, orientation and the optics decision table

Machine-local roots (not needed elsewhere; copy them before switching machines if they are wanted):
- `C:\TasOut\s09\acc2`: the revision-2 acceptance and negatives, packed into `../runs`, `../negatives` and
  `../summary-*.json`.
- `C:\TasOut\s09\rev2-ab`: the before/after runs below.

| Build | Harness `os-input-readback.dll` | Library `SAM.Analytical.OpenStudio.InputReadBack.dll` (= native.json `ReaderAssemblySha256`) |
|---|---|---|
| Reviewed head `d6d1cc5b` | `177d2b87e99f96dae7d371cbd6719db5cd01149366c80a2bc4a48864741f8b92` | `4c0cea58317ff68dd23e2eb821cc01bef6859989ffc398a02ce50ca5ea47b493` |
| Revision 2 (tested content) | `32090bbf4d7242fb5c9a1d4b176c7880aec1f214215529f66f76c8e114189a5a` | `5f279f12cb2b746850ac1bdb40cb1f9f9125e3b3d639fa195cb8a0a3da468549` |

These are local Release x64 builds. A .NET DLL hash identifies a build on this machine only.

## `ab/`: the same inputs, reviewed head against revision 2

Each input is a byte copy of a run folder (`frozen-model.json`, `weather.epw`, `model.osm`, `in.idf`, `export.json`).
Each binary runs `read --run <dir>` and then
`compare --run <dir> --fixture-id <id> --rule-set approved --out <dir>\compare-approved`, each a fresh process.
`ab/base/<input>/` and `ab/rev/<input>/` hold the two `report-approved.txt` files.

| Input | Mutation (IDF / OSM sha256 prefix) | Reviewed head | Revision 2 |
|---|---|---|---|
| box | none, acc1 export (`737ea1f4…` / `53eafa9d…`) | BLOCKED owner, exit 12, 316/316 | BLOCKED owner, exit 12, 316/316 |
| two | none, acc1 export (`f0b9ca4b…` / `fc3114a7…`) | BLOCKED owner, exit 12, 586/586 | BLOCKED owner, exit 12, 586/586 |
| reviewer-zone-multiplier | the reviewer's copy: IDF Zone Multiplier 1 → 2 (`3e058814…`) | exit 12 | **FAIL, exit 10** |
| reviewer-window-multiplier | the reviewer's copy: both IDF window Multipliers → 2 (`87ed8ff5…`) | exit 12 | **FAIL, exit 10** |
| reviewer-building-north | the reviewer's copy: IDF Building North Axis → 90 (`ecafa1fb…`) | exit 12 | **FAIL, exit 10** |
| neg-zone-multiplier | IDF Zone Multiplier 1 → 2, OSM unchanged | exit 12 | **FAIL, exit 10** |
| neg-zone-multiplier-both | as above, plus OS:ThermalZone Multiplier → 2 | exit 12 | **FAIL, exit 10** |
| neg-window-multiplier | IDF window Multipliers → 2, OSM unchanged | exit 12 | **FAIL, exit 10** |
| neg-window-multiplier-both | as above, plus OS:SubSurface Multipliers → 2 | exit 12 | **FAIL, exit 10** |
| neg-north-axis | IDF Building North Axis → 90, OSM unchanged | exit 12 | **FAIL, exit 10** |
| neg-north-axis-both | as above, plus OS:Building North Axis → 90 | exit 12 | **FAIL, exit 10** |

- **The reviewer's copies:** their IDFs differ from the committed box IDF only in the mutated lines. Their
  `export.json` carries the reviewer's mutation label.
- **The `-both` cases:** the OSM and IDF agree, so the only consistency entries left are the checks against SAM intent
  (`CONSISTENCY FAIL: multiplicity: … SAM states one … and no multiplier` and
  `orientation: Zone '…' is rotated by 90 deg …; SAM states no rotation`).
- **The unchanged fixtures:** the outcome counts are identical between the two binaries.

Reproduce, from the repository root:
1. Build `benchmark/SAM.Analytical.OpenStudio.InputReadBack.Cli` (Release x64) at each commit.
2. Generate each `neg-*` copy with the revision binary:
   `os-input-readback negative --run <acc>/box --case <case> --out <dir>`.
3. Run `read` and `compare` with each binary as above.

## `optics-table/` and `optics-*.txt`: the OS-MAT-SINGLE-SIDED decision table

`optics-table/` is a small evidence program. It is not part of the solution or CI. It runs the comparator
(`OsComparison.Run`, approved rules) on a run's `native.json` and prints every opaque layer of every zone side:
- which face is exposed;
- the SAM face that is physically needed there;
- SAM's external and internal values, the native values, and the delta at the exposed face;
- how many of that layer's internal-side rows are listed MAPPING-DIFFERENCE.

```text
dotnet run -c Release -p:Platform=x64 --project documentation/S09-OpenStudio-InputReadBack-evidence/revision-2/optics-table -- benchmark/SAM.Analytical.OpenStudio.InputReadBack/Fixtures/s08-fix-box.json C:\TasOut\s09\acc2\box\native.json C:\TasOut\s09\acc2\box\weather.epw s08-fix-box
```

Results (the same on the acc2 and the A/B revision runs):
- **box:** 69 rows; 18 at an exposed face where SAM needs the internal value, 51 on buried, ground or
  SAM-external faces.
- **two:** 138 rows; 33 exposed, 105 buried.

The record (`../../S09-OpenStudio-InputReadBack-PR.md`) has the compact table and the EnergyPlus 25.1 grounding.
