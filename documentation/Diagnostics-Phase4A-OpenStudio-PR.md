# Unified Diagnostics, Phase 4A (SAM_OpenStudio integration) - PR record

Base: `sow/2026-Q4` (`fb21e03`). Branch: `feature/openstudio-diagnostics-phase4a-q4`. Dependencies: SAM `sow/2026-Q4` with SAM#201 / SAM#202 (`SAM.Core.Diagnostic`, `Diagnostics`, `DiagnosticSeverity`, `DiagnosticDetail`); no SAM, SAM_OCCT or SAM_Tas change. TAS is Phase 4B and is not touched.
Validated content SHA: `5cb20dde05d39c51b1a98fab6876139a4d5fa419` (code and tests, after the review findings). Record-only commits follow; the final PR head and hosted checks are in the PR description.
Draft. Merge needs owner approval (AGENTS.md). `PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status
Implementation complete and revised after the owner-accepted review (nine findings, below). Local Windows build and tests were run **with the OpenStudio 3.10.0 CLI installed**, on this head and on base. Not run: interactive Rhino / Grasshopper. Windows acceptance list at the end.

## Investigation (what was found)
- One diagnostic type, `OpenStudioDiagnostic` (`Code`, `Severity` Information/Warning/Error, `Message`, `SamGuid`, `SamObjectType`, `OpenStudioObjectName`), 17 forward codes (`SAM-OS-*`) and 31 import codes (`SAM-OSI-*`). Severity is a literal chosen at each production site (about 283), never inferred from text.
- Two funnels: `OpenStudioConversionContext.AddDiagnostic` and `OpenStudioImportContext.AddDiagnostic` (statistics and `HasErrors`/`IsValid` hang off them), plus `OpenStudioSimulationRunner.Run(path, ...)` and the geometry validators, which fill a plain `IList<OpenStudioDiagnostic>`.
- Users see them as the `diagnostics` string list of `SAMAnalyticalToOpenStudio` (1.3.1), `OpenStudioRunModel` (1.2.0) and `OpenStudioToSAMAnalytical` (1.0.0): every diagnostic's `ToString()`. Only `OpenStudioToSAMAnalytical` raises canvas Warning/Error balloons; the other two raise none for diagnostics.
- OpenStudio diagnostics have no detail dimension today: every line, Information included, is always shown. The legacy `diagnostics` outputs therefore correspond to `DiagnosticDetail.Detailed`.
- Refusals and failures are already structured: `AnalyticalModel has no AdjacencyCluster` (Error), conversion Errors (the run is skipped with a Warning "Simulation not executed"), missing output directory / input / EPW / `openstudio.exe`, CLI failure and unreadable SQL (`SAM-OS-RUN-001` Error), import `SAM-OSI-IN-001/002`, `OSM-001`, `OSW-001..006` (Errors). `Import.AppendRunDiagnostics` downgrades a `RunCliFailed` Error to Information (`SAM-OSI-OSW-009`) when the model was produced; the adapter reads severity as emitted.
- **Information call sites (all 51 `Severity.Information` sites read for the revision).** The producers use one code for several situations, e.g. `SAM-OS-MAT-002` covers both a single-sided-optics approximation and an inert vapour diffusion factor, `SAM-OS-IC-001` both an air-panel exchange assumption and an unused view coefficient, `SAM-OS-RUN-002` both weather/design-day basis and ground temperatures.

## Work completed
New in `SAM_OpenStudio/SAM.Core.OpenStudio` (additive, `Query` partial class and one class):
- `OpenStudioDiagnosticRecord`: the original `OpenStudioDiagnostic` plus its `Diagnostic`; `Text` is the original `ToString()`, computed once. The constructor throws if the `Diagnostic` does not describe the `Source` (code, severity, detail, message), so an inconsistent record cannot exist. `SamGuid`, `SamObjectType`, `OpenStudioObjectName` are exposed separately.
- `Query.ToDiagnosticSeverity` (Information to Info, Warning, Error one to one; an undefined value is Error), `Query.GetDiagnosticDetail`, `Query.ToDiagnostic(sourceIndex)`, `Query.CreateDiagnosticRecords`, `Query.FilterDiagnosticRecords` (through a real `Core.Diagnostics`), `Query.ToDiagnostics(detail = Detailed)`, `Query.FormatDiagnostics(detail = Detailed)`.
- `OpenStudioConversionResult.GetDiagnostics(detail = Detailed)` and `OpenStudioImportResult.GetDiagnostics(detail = Detailed)` in `SAM.Analytical.OpenStudio`.

Unchanged: all existing members and signatures, codes, `OpenStudioDiagnostic.ToString()`, statistics, `IsValid`, converters, runner, `Grasshopper/` (GUIDs, versions, inputs, outputs, balloons), the benchmark project, SAM.Core, every engineering result.

## Review findings (owner-accepted) and how they were resolved
| # | Finding | Resolution |
|---|---|---|
| 1 | Summary hid Information approximations because detail was a coarse code table | Information detail revised against every call site (below). `GetDiagnosticDetail` documents the principle; every site is pinned in `DiagnosticDetailTableTests` (52 rows) and exercised through the real converters |
| 2 | The structured diagnostic lost the OpenStudio object name; a null message became "" | `Subject` = `"{SamObjectType} {SamGuid} (OpenStudio {name})"`, or either part alone (`SAM.Core.Diagnostic` has one free-text subject; no SAM.Core change); the record exposes the three values separately. `Diagnostic` stores a null message as "" (its constructor); the legacy diagnostic and its text keep the null (`"[C] Warning: "`). Tested |
| 3 | Undefined severity vs `IsValid` | Behaviour unchanged and now documented on `ToDiagnosticSeverity` and both `GetDiagnostics`: an undefined severity is Error structurally but `IsValid`, `HasErrors` and the statistics ignore it. `UndefinedSeverity_IsAnErrorStructurally_ButLegacyIsValidAndStatisticsAreUnchanged` asserts both sides through a real context and result |
| 4 | `Format()` vs `FormatDiagnostics()` | XML docs on `ToDiagnostics`, `FormatDiagnostics` and both `GetDiagnostics`; `StructuredFormat_IsNotTheLegacyFormat` asserts the structured text has no code and `FormatDiagnostics` equals the legacy `ToString()` |
| 5 | Real-run test | `RealRun_ResultsAreUnchangedByTheAdapters`: asserts `RunResult`, success and `Loads` before reading, disposes the result, one simulation, hashes the saved OSM and the SQL before and after the adapters ran. The second simulation was dropped: it only re-tested EnergyPlus determinism, which the adapters cannot influence (and EnergyPlus is not bit-reproducible run to run, see below) |
| 6 | Fixed code counts | Replaced by `DiagnosticCodes_ReleasedValuesAreStable_...`: every released constant (name and value, 17 + 31) must still exist unchanged; new codes allowed; values unique, correctly prefixed, no forward/import collision |
| 7 | Inconsistent record | Constructor validates (see above); legacy text unchanged. `Record_CannotBeInconsistent` covers null arguments and each mismatch |
| 8 | Baseline dates and counts | Corrected below; verified, historical and inferred figures are separated |
| 9 | Allocation | Measured, **not changed**: for 20,000 diagnostics `FormatDiagnostics` took about 78 ms and 28 MiB per call, a single-pass variant about 25 ms and 14 MiB. A real model has tens to hundreds of diagnostics, no component calls it, and a second implementation of the filter rule would have to be kept in step with `Core.Diagnostics`. The part that was cheap and safe was done: the record caches its text instead of recomputing it |

## Information detail (revised table)
Principle: a line that records an approximation, fallback, assumption, dropped input or data source that can change simulation results is **Summary**. Severity, code, message and order are never changed by the level; Warnings and Errors are Summary-level and never dropped.
- **Summary**: `SAM-OS-RUN-001` (site override), `RUN-002` (weather, design-day and ground-temperature basis), `SET-001` (measures, injected IDF), `CON-001` (SimpleGlazingSystem fallback), `SCH-001` (leap-year profile), `MAT-002`, `CON-002`, `IC-001` (including the air-panel exchange assumptions), `HVAC-001`; import `OSM-002`, `OSW-007`, `OSW-008`, `OSW-009`, `SET-001`, `APX-001`, `LOAD-001`, `WEA-001`.
- **Lowered, known inert (explicit message prefix, only Information of that code)**: `MAT-002` "Vapour diffusion factor" (Detailed; needs HAMT/EMPD, not used), `IC-001` "Occupancy view coefficient" and "Equipment view coefficient" (Detailed; daylighting fields), `CON-002` "Frame data on an opaque door" (Standard). The wording of these lines is already pinned against the real converters by `DeclaredDiagnosticsTests` (C7) and again by `RealConverters_*`. A reworded or unknown message under these codes stays at Summary, so a change of wording can only add lines.
- **Standard**: `GEO-003` (non-convex advisory under PixelCounting), `ADJ-001` (derived building stories), import `ZONE-001`, and every code added later.
- **Detailed**: per-object clean-ups `SAM-OS-GEO-002`, `SAM-OSI-GEO-002`.
- Decision rationale: the converters cannot be changed in this PR, so a code-only table cannot separate "inert" from "material" lines under the shared codes; the shared codes therefore default to visible and only the four inert lines are lowered by message prefix.

## Decisions and assumptions
- **No new Grasshopper input** and **no new canvas balloons** (owner decisions): the legacy `diagnostics` outputs are unchanged; filtering exists only on the library API. No `diagnosticDetail_`, no version bumps.
- `Diagnostic.ToString()` (`Severity: Subject: Message`) is a different format from the legacy text and is not used for the legacy outputs. The record keeps the legacy text.
- `SourceIndex` = position in the source list (null entries are skipped but still counted). `FormatDiagnostics` skips null entries (the legacy loops would throw on a null).
- Not covered: the string-only producers `Create.DesignDays` and `Modify.AddResults` (no producer severity; their two legacy components keep their Warning mapping).
- `C7/DeclaredDiagnosticsTests.SingleBoxModel` changed from `private` to `internal` so the new tests reuse its fixture; no behaviour change.

## Files changed
New: `SAM_OpenStudio/SAM.Core.OpenStudio/Classes/OpenStudioDiagnosticRecord.cs`; `SAM_OpenStudio/SAM.Core.OpenStudio/Query/{ToDiagnosticSeverity,GetDiagnosticDetail,ToDiagnostic,CreateDiagnosticRecords,FilterDiagnosticRecords,ToDiagnostics,FormatDiagnostics}.cs`; `tests/SAM.Analytical.OpenStudio.Tests/D1/DiagnosticsAdapterTests.cs` (27 tests) and `D1/DiagnosticDetailTableTests.cs` (9 tests); this record.
Modified: `SAM_OpenStudio/SAM.Analytical.OpenStudio/Classes/{OpenStudioConversionResult,OpenStudioImportResult}.cs` (one added method each); `tests/.../C7/DeclaredDiagnosticsTests.cs` (one visibility keyword).
`git diff --stat fb21e03 HEAD -- Grasshopper benchmark SAM_OpenStudio/SAM.Geometry.OpenStudio` is empty; the SAM repository working tree is clean.

## Validation (local Windows, machine-local; not hosted CI)
Windows 11, .NET SDK 10.0.401 (tests target net8.0), SAM built from `sow/2026-Q4` (`SAM\build\SAM.Core.dll` contains the Diagnostics types). **OpenStudio 3.10.0 CLI at `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe`, found automatically.**
Commands (from the repo root): `dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64`, then `dotnet test <same project> -c Release -p:Platform=x64 --no-build` and `dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64`.

**Verified, executed in this session (CLI installed)**

| Check | Base `fb21e03` (separate worktree, removed afterwards) | Head `5cb20dd` |
|---|---|---|
| Build, Release x64 | 0 errors | 0 errors |
| Functional suite | 352 passed, 0 failed, 2 skipped (354) | **388 passed, 0 failed, 2 skipped (390)** = 352 + 36 new |
| Benchmark tests (no filter) | 43 passed | 43 passed |
| New tests | - | 27 + 9 = 36, all pass, including the real EnergyPlus run |
| Exported OSM, diagnostics text, statistics | see below | identical |

The 2 skips are the optional SQL fixture, on base and head.

**Base-vs-head regression (one harness, machine-local, not committed).** The same 6 fixture models (`SingleBox`, `TwoAdjacentBoxes`, `OpaqueAirGapBox`, `LShapedBox`, `TwoStackedBoxes`, `DegeneratePanelBox`) converted on base and on head with the EPW: the saved OSM text (357,288 lines, OpenStudio handles, SAM Guids and their 8-digit name suffixes normalised, worktree path normalised), the full legacy diagnostics text and order, `IsValid` and statistics per model are **identical**. The one real EnergyPlus run (`SingleBox`) gives heating 2924.8143028716945 kWh / cooling 1758.0131760410147 kWh on base and 2924.8143028717136 / 1758.0131760410102 on head: a relative difference of about 6e-14. Two runs of the *same head* differ by the same order (2924.8143028717136 vs 2924.814302871705), so this is EnergyPlus run-to-run floating-point noise, not a code difference (the harness did not call the adapters). The committed real-run test therefore compares file hashes and values taken after a single run, not two runs.

**Mutation checks (temporary edits, restored; the working tree matches the commit)**: a Summary code dropped from the table (2 failing tests), inert match widened to any message (5), `MAT-002` dropped from Summary (5), subject without the OpenStudio name (3), record validation removed (1), undefined severity mapped to Info (2), filter dropping Warnings (7). Earlier round: Warning mapped to Info, bad detail level, undefined severity, filter dropping Warnings.

**Historical, not re-run here**: the 2026-10-11 closeouts (icon migration and current-coverage, `PROJECT_PROGRESS.md`) record the same base figures, 352 / 0 / 2 and 41 benchmark tests; base was re-run today and matches (functional) or exceeds (benchmark 43, one more test than recorded) them.
**Earlier CLI-less run (superseded)**: before the CLI was installed, base ran 328 passed / 24 failed / 2 skipped and the first head 348 / 24 / 2 with the identical 24 failing (all `openstudio.exe`-dependent). Superseded by the CLI runs above.
**Not verified**: the behaviour of the new real-run test without the CLI (it uses `Assume`, expected to skip; not executed here). Hosted CI runs only the benchmark producer tests, not the functional suite; its result for the final head is in the PR description.

## Not run / limitations
- The Grasshopper projects were **not built locally** (their post-build step deploys into `%APPDATA%\SAM`); their source is unchanged, hosted CI builds them. No interactive Rhino / Grasshopper check; component outputs were not compared on a canvas.
- The adapters are not consumed by any component yet; nothing reads the Information detail table in production.
- The detail table and the four inert prefixes are an engineering judgement from the producer call sites; a producer that is reworded stays visible (safe direction), a new Information site under an unlisted code is Standard until classified.
- A diagnostic with an undefined severity is an Error structurally but not for `IsValid` (documented, tested, legacy behaviour kept).
- Performance of `FormatDiagnostics` was measured and left unchanged (finding 9).

## Windows acceptance (exact requirements)
1. SAM `sow/2026-Q4` built Release (`SAM\build\SAM.Core.dll` contains `SAM.Core.Diagnostic`); `dotnet build SAM_OpenStudio.sln -c Release -p:Platform=x64` (this also builds both GH projects and deploys to `%APPDATA%\SAM`, as before).
2. With `openstudio.exe` installed: `dotnet test tests/SAM.Analytical.OpenStudio.Tests -c Release -p:Platform=x64`. Expect 390 tests, 388 passed, only the 2 SQL-fixture skips (done locally 2026-10-15; repeat on the final PR head).
3. In Grasshopper (Rhino 8, metre document), on base and on this branch with the same model and EPW: run `SAMAnalyticalToOpenStudio` with `_run` true; `osmPath`, `oswPath`, `sqlPath`, `successful` identical, `heating` and `cooling` equal to within floating-point noise (about 1e-13 relative between two runs), and `diagnostics` byte-identical (text and order). Repeat `OpenStudioRunModel` and `OpenStudioToSAMAnalytical` (OSM and OSW).
4. Open a saved definition containing the three components: loads, wires intact, no "update component" mark (no version change).

## Next step
Owner review of the draft PR; Windows acceptance steps 3 and 4 in Rhino. After merge: `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4` (docs-only, `[skip ci]`). Follow-ups, each its own PR and owner decision: canvas Warning/Error balloons for `SAMAnalyticalToOpenStudio` and `OpenStudioRunModel`; optional hidden `diagnosticDetail_`; Phase 4B (TAS). Recommended for the next piece: Opus, high effort, new session, Plan mode.
