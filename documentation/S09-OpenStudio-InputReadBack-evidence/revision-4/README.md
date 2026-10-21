# S09 revision 4 evidence: the final-head CI failure at `e0a6ea19`

Build (Windows) run 39603021836 on the revision-3 head `e0a6ea1950455e04dbf086d164500fd8829867ec` failed 1 of the 58
S09 tests: `EveryTargetedMutation_Fails("neg-people-density","s08-fix-box")`, `DirectoryNotFoundException` loading
`compare-approved/report.json` (`ci-39603021836-excerpt.txt`). The benchmark tests (51/51) and SPDX run 39603021804
passed. Every dependency resolved to `sow/2026-Q4` (SAM_Tas `c50e9169`). Revision 4 (code `0c232875`) is the response.

## What the CI run shows, and what it does not

- `compare` wrote no `compare-approved` directory. `OsReport.Save` creates that directory first, so `compare` returned
  before `Save`. In `ReadBackCompare.Run` that happens only for exit 64 (usage), exit 70 from the infrastructure checks,
  or exit 70 from an exception caught in `Program.Main`. The preceding `read` step returned 0 (the test asserts it).
- The test captured `compare`'s exit code and output but loaded `report.json` before checking it, so the exit code and
  stderr were lost. No diagnostic artifact was kept. **Which of those branches ran in CI is therefore not known.**
- The failed test took 695 ms. The other box negatives in that run took "1 s" each, about 1.5 s apart. This fits a
  `compare` that stopped before comparing, but does not prove a branch.

## The defect found: process freshness was judged by process id alone

`ReadBackCompare.Infrastructure` (reviewed head) refused a read as infrastructure (exit 70) when the reader's id
equalled the export's id, or the comparison's id equalled the reader's. Windows hands out the id of an exited process
again, so an equal id does not show the same process:
- A negative copy keeps the `export.json` of the original export (the test fixture's one-time set-up), whose process
  exited long before. A later reader can be given that id. The same holds between a reader and the next `compare`.
- `pid-reuse.ps1` launches the harness 1000 times in sequence, as `TestSupport.Cli` does. On this workstation it got
  **141 ids that an earlier launch already had**, at least 164 launches apart (`pid-reuse-1000.txt`). A 200-launch run
  got 0 and an earlier one 4: reuse depends on machine load. It was not measured on a GitHub runner, so how often it
  hits the S09 suite there is not known.

**Reproduced locally** (`simulate-pid-reuse.ps1`, `ab-*-summary.txt`). The state that id reuse produces is a
negative copy whose `export.json` id equals its reader's id, while the reader started after the export finished. The
script makes that state by writing the reader's id into the copy's `export.json`:

| Binary (local Release x64) | `control` copies (as made) | `reused` copies (reader's id written into export.json) |
|---|---|---|
| reviewed head source `e0a6ea19`, `os-input-readback.dll` `5041645c…` | FAIL, exit 10, report written (3/3) | **exit 70, `infrastructure: the reader did not run in a fresh process after the export`, no `compare-approved` (3/3)**, 175–233 ms |
| revision 4 `0c232875`, `25a35b92…` | FAIL, exit 10 (3/3) | **FAIL, exit 10, report written (3/3)**; `report.txt` byte-identical to the controls, to the base controls and to the acc3 `neg-people-density` report |

The reviewed head's result is the CI signature: exit before `Save`, no `compare-approved`, and the test's
`DirectoryNotFoundException`. With the revision-4 diagnostics the same state reports the cause directly
(`old-check-diagnostics.txt`).

**Conclusion.** The id-only check is a defect: it refuses a fresh process whose id was reused, and that state produces
exactly the CI failure. It is the only mechanism found that matches the evidence. That it *was* the CI cause is likely
but unproven, because the CI run kept no output. An exception inside `compare` is not ruled out by CI evidence. Locally,
none of 319 earlier test runs and none of the acc3/acc5/acc6 runs lacked a report.

## Change (code `0c232875`)

- `ProcessIdentity` (in `Classes/ExportManifest.cs`): a process is its id **and** its OS creation time.
  - `export.json` (schema `s09-export/2`) and `native.json` (schema `s09-native/2`) record `ProcessStartedUtc`.
  - Reader and export are the same process only when both match. The reader must still start after the export
    finished, as before.
  - A missing identity is an infrastructure failure ("the export or reader process identity is not recorded"), so
    older (`/1`) files and hand-made ones fail closed.
  - The comparison is refused when its own id and creation time are the reader's.
  - `report.json` Inputs add `exportProcessStartedUtc`, `readerProcessStartedUtc` and `compareProcessStartedUtc`.
    `report.txt` is unchanged.
- `TestSupport.ReadAndCompare` asserts `report.json` exists before parsing it. A failed `read` or `compare` reports:
  - the exit code and labelled stdout/stderr (16,000 characters at most);
  - both process identities and times;
  - the run's file list.

  It writes the same text to `s09-diagnostics/<run>-<step>.txt`. The Build workflow uploads those `.txt` files only
  when a step fails (`<repo>-s09-diagnostics`, 30 days). No native files are uploaded.
- Not changed: the hash checks, fixture bytes, weather, tolerances, the shared core, the writer, and every existing
  negative and assertion. No retry was added.

## Results

| Check | At | Result |
|---|---|---|
| S09 tests | `0c232875` (harness `25a35b92…`, library `006668df…`) | **62/62**: the 58 existing, unchanged, plus 4 new: `AReusedProcessId_IsNotTheExportProcess` (FAIL, exit 10, report) and `AReadNotProvenFresh_IsAnInfrastructureFailure` × 3 (same process, identity not recorded, read before the export finished: exit 70, no report) |
| The 4 new tests with the reviewed head's id-only check restored (local edit, then restored and rebuilt) | working tree | `AReusedProcessId_IsNotTheExportProcess` **fails** (exit 70); the 3 refusal cases pass under both |
| Benchmark tests (not Simulation) | `0c232875` | 51/51 |
| Acceptance and negatives, `C:\TasOut\s09\acc6` | harness `25a35b92…` | box and two BLOCKED owner (12), 316/316 and 586/586; box repeat IDF identical; 16/16 negatives FAIL (10); originals unchanged. All 19 `report.txt` and the 3 IDFs byte-identical to acc3 |
| Before/after, `C:\TasOut\s09\rev4-ab` | `5041645c…` vs `25a35b92…` | see the table above |

- `C:\TasOut\s09\acc5` is the same acceptance with the pre-commit working-tree build (`8601ba19…`, the same source
  with LF endings). It gave the same results.
- `C:\TasOut\s09\acc4-discarded-old-check-binary` was run by mistake with the temporary id-only build and is not
  evidence.
- The committed `../runs`, `../negatives` and `../summary-*.json` stay the acc3 pack (schema `/1`, harness `b7cf9f26…`).
  Their `report.txt` content is what acc6 reproduces.

Reproduce, from the repository root in Windows PowerShell (the EPW as in the PR record):
1. Build `benchmark/SAM.Analytical.OpenStudio.InputReadBack.Cli` (Release x64, `dotnet build --no-incremental`) at
   `e0a6ea19` and at `0c232875`, and copy each `bin/x64/Release/net8.0` folder (`bin-base`, `bin-rev`).
   - Only `Export.cs`, `ReadBackRead.cs`, `ExportManifest.cs` and `NativeReader.cs` differ between them
     (`git diff --name-only e0a6ea19 0c232875`).
   - `dotnet test` does not accept `--no-incremental` with SDK 10.0.401; build first, then `dotnet test --no-build`.
2. `simulate-pid-reuse.ps1 -Exe <bin>\os-input-readback.exe -Model benchmark/SAM.Analytical.OpenStudio.InputReadBack/Fixtures/s08-fix-box.json -Epw <budapest.epw> -Out <new dir>`
   for each binary.
3. `pid-reuse.ps1 -Exe <bin>\os-input-readback.exe -Count 1000`.

## Observation, not changed here

SAM_Tas `SAM_Tas/SAM.Analytical.Tas.DirectT3D.Validation/ReadBack/ReadBackCompare.cs:159` (S08, `c50e9169`) also
refuses a read whose reader id equals the export id. Whether id reuse can reach it depends on how S08 runs its steps,
which was not assessed, and the S08 check was not exercised here. SAM_Tas is read-only for S09; any change there is a
separate task.
