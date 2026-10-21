# S09 revision 3 evidence: missing OSM counterparts

The independent review of `6f7e6b7d` found that `OsComparison.Multiplicity` skipped the OSM/IDF multiplier check
when an IDF `Zone` or `FenestrationSurface:Detailed` had no `OS:ThermalZone` / `OS:SubSurface` of its name. Revision 3
(tested content `c1e53328`) claims that consistency only against one identified OSM counterpart.

Machine-local roots (not needed elsewhere; copy them before switching machines if they are wanted):
- `C:\TasOut\s09\acc3`: the revision-3 acceptance (3 runs) and the 16 negatives, packed into `../runs`, `../negatives`
  and `../summary-*.json`.
- `C:\TasOut\s09\rev3-ab`: the before/after below, with both binaries (`bin-base`, `bin-rev`), the inputs and every
  native, read and compare file.

| Build | Harness `os-input-readback.dll` | Library `SAM.Analytical.OpenStudio.InputReadBack.dll` (= native.json `ReaderAssemblySha256`) |
|---|---|---|
| Reviewed head `6f7e6b7d` (code = `cfe097f4`) | `9702cecb9b66a9cc1a6cbd515c50e4d11e80c430f019994a2a29f129da36e961` | `0d091ffa77e8880be95224a3249617643041f7f585c74b8af0374bbbbed578e6` |
| Revision 3 (`c1e53328`) | `b7cf9f2684f09ade91d2fdbbae3de1a9b069dde35359893d0b42f1295f3aa11d` | `ce8270dd002e63cb4be574de24c6f5fce090ba3ca0207e3d73c7db2ba358ca70` |

These are local Release x64 builds (`--no-incremental`). A .NET DLL hash identifies a build on this machine only; the
reviewed-head hashes differ from the revision-2 table because they are a fresh build of the same source.

## `ab/`: the same inputs, reviewed head against revision 3

Each input is a byte copy of a run folder (`frozen-model.json`, `weather.epw`, `model.osm`, `in.idf`, `export.json`).
`run-ab.ps1` copies it again per binary and runs `read` and then `compare` (approved rules), each a fresh process.
`ab/base/<input>/` and `ab/rev/<input>/` hold the two `report-approved.txt`; `ab/*-summary.txt` hold one line per input.

| Input | Mutation (IDF unchanged in every case) | Reviewed head | Revision 3 |
|---|---|---|---|
| box | none (acc2 export; IDF `737ea1f4…`, OSM `98bcd7ce…`) | BLOCKED owner, exit 12, 316/316 | the same; identical outcome counts and report text |
| two | none (acc2 export; IDF `f0b9ca4b…`, OSM `db42485a…`) | BLOCKED owner, exit 12, 586/586 | the same; identical outcome counts and report text |
| box-no-thermalzone | reviewer-style: every `OS:ThermalZone` removed (1; OSM `1a53f105…`) | exit 12, 316/316, no Consistency/Unsupported | **FAIL, exit 10**, 316/316; 1 Consistency |
| box-no-subsurfaces | reviewer-style: every `OS:SubSurface` removed (2; OSM `d9dbb1f6…`) | exit 12, 316/316, none | **FAIL, exit 10**, 316/316; 2 Consistency |
| two-no-thermalzone | reviewer-style: every `OS:ThermalZone` removed (2; OSM `33f93224…`) | exit 12, 586/586, none | **FAIL, exit 10**, 586/586; 2 Consistency |
| two-no-subsurfaces | reviewer-style: every `OS:SubSurface` removed (1; OSM `411c0bee…`) | exit 12, 586/586, none | **FAIL, exit 10**, 586/586; 1 Consistency |
| box-neg-osm-missing-subsurfaces | the CLI negative, from the acc3 box run (OSM `1d2821a3…`) | exit 12, 316/316, none | **FAIL, exit 10**, 316/316; 2 Consistency |
| two-neg-osm-missing-thermal-zone | the CLI negative: zone A's `OS:ThermalZone` only, from the acc3 two run (OSM `93bead41…`) | exit 12, 586/586, none | **FAIL, exit 10**, 586/586; 1 Consistency |

- **Every revision-3 Consistency entry has the form** `multiplicity: IDF Zone '<name>' has no OS:ThermalZone in the
  OSM (none of its N OS:ThermalZone object(s) has that name); the OSM and IDF disagree`, or the same for
  `FenestrationSurface:Detailed` / `OS:SubSurface`. There are no Unsupported entries, and the outcome counts are those
  of the unchanged fixture: only the verdict changes.
- **The unchanged fixtures:** `report-approved.txt` is byte-identical between the two binaries and to the committed
  acc2/acc3 reports. In `report.json` only the multiplicity Supporting line differs: it now states
  `1 IDF zone(s) (1 with their OS:ThermalZone) … 2 IDF subsurface(s) (2 with their OS:SubSurface)` (box) and
  `2 … (2 …) … 1 … (1 …)` (two).
- **The reviewer-style copies** were made with `remove-osm-objects.sh`. It deletes every object of one OSM class and
  rewrites `export.json` `OsmSha256` (and its `Mutation` label), as the review describes. The scripts are kept here as
  they were run, with a licence header added.

Reproduce, from the repository root (Git Bash for the `.sh`, Windows PowerShell for the `.ps1`):
1. Build `benchmark/SAM.Analytical.OpenStudio.InputReadBack.Cli` (Release x64, `--no-incremental`) at `6f7e6b7d` and at
   `c1e53328`, and copy each `bin/x64/Release/net8.0` folder (`bin-base`, `bin-rev`).
2. Copy an acceptance run's five files into `inputs/<box|two>[-suffix]` and apply
   `remove-osm-objects.sh <dir> OS:ThermalZone` or `… OS:SubSurface`. The two CLI negatives come from
   `os-input-readback negative --run <acc>/<box|two> --case <case> --out <dir>` with the revision binary.
3. `run-ab.ps1 -Bin bin-base -Out base` and `run-ab.ps1 -Bin bin-rev -Out rev`. The fixture id follows the input
   name's `box`/`two` prefix.

## Observation, not changed here

A copy of the box run with `OS:Building` duplicated makes `compare` throw in `OsComparison.Orientation`
(`SingleOrDefault`) and exit 70. That is the infrastructure-failure code, so it fails closed, but not as a named BLOCKED
technical entry. It is outside this finding and is left as it is (`C:\TasOut\s09\rev3-ab\probe-osm-building-duplicated`).
