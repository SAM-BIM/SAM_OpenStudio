# Unified Diagnostics, Phase 4A (SAM_OpenStudio integration) - PR record

Base: `sow/2026-Q4` (`fb21e03`). Branch: `feature/openstudio-diagnostics-phase4a-q4`. Dependencies: SAM `sow/2026-Q4` with SAM#201 / SAM#202 (`SAM.Core.Diagnostic`, `Diagnostics`, `DiagnosticSeverity`, `DiagnosticDetail`); no SAM, SAM_OCCT or SAM_Tas change. TAS is Phase 4B and is not touched.
Validated content SHA: `d32658c7a2bda763072818f426cf658b7fc897c9` (code and tests). A record-only commit follows; the final PR head and hosted checks are in the PR description.
Draft. Merge needs owner approval (AGENTS.md). `PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status
Implementation complete; local Windows build and tests done (below). **Not run:** tests that need `openstudio.exe` / EnergyPlus (not installed on the build machine), interactive Rhino / Grasshopper. Windows acceptance list below.

## Investigation (what was found)
- One diagnostic type, `OpenStudioDiagnostic` (`Code`, `Severity` Information/Warning/Error, `Message`, `SamGuid`, `SamObjectType`, `OpenStudioObjectName`), 17 forward codes (`SAM-OS-*`) and 31 import codes (`SAM-OSI-*`). Severity is a literal chosen at each production site (about 283), never inferred from text.
- Two funnels: `OpenStudioConversionContext.AddDiagnostic` and `OpenStudioImportContext.AddDiagnostic` (statistics and `HasErrors`/`IsValid` hang off them), plus `OpenStudioSimulationRunner.Run(path, ...)` and the geometry validators, which fill a plain `IList<OpenStudioDiagnostic>`.
- Users see them as the `diagnostics` string list of `SAMAnalyticalToOpenStudio` (1.3.1), `OpenStudioRunModel` (1.2.0) and `OpenStudioToSAMAnalytical` (1.0.0): every diagnostic's `ToString()`. Only `OpenStudioToSAMAnalytical` raises canvas Warning/Error balloons; the other two raise none for diagnostics.
- OpenStudio diagnostics have no detail dimension today: every line, Information included, is always shown. The legacy `diagnostics` outputs therefore correspond to `DiagnosticDetail.Detailed`.
- Refusals and failures are already structured: `AnalyticalModel has no AdjacencyCluster` (Error), conversion Errors (the run is skipped with a Warning "Simulation not executed"), missing output directory / input / EPW / `openstudio.exe`, CLI failure and unreadable SQL (`SAM-OS-RUN-001` Error), import `SAM-OSI-IN-001/002`, `OSM-001`, `OSW-001..006` (Errors). `Import.AppendRunDiagnostics` downgrades a `RunCliFailed` Error to Information (`SAM-OSI-OSW-009`) when the model was produced; the adapter reads severity as emitted.

## Work completed
New in `SAM_OpenStudio/SAM.Core.OpenStudio` (additive, `Query` partial class and one class):
- `OpenStudioDiagnosticRecord`: the original `OpenStudioDiagnostic` plus its `Diagnostic`; `Text` is the original `ToString()`.
- `Query.ToDiagnosticSeverity` (Information to Info, Warning, Error one to one; an undefined value is Error), `Query.GetDiagnosticDetail`, `Query.ToDiagnostic(sourceIndex)`, `Query.CreateDiagnosticRecords`, `Query.FilterDiagnosticRecords` (through a real `Core.Diagnostics`), `Query.ToDiagnostics(detail = Detailed)`, `Query.FormatDiagnostics(detail = Detailed)`.
- `OpenStudioConversionResult.GetDiagnostics(detail = Detailed)` and `OpenStudioImportResult.GetDiagnostics(detail = Detailed)` in `SAM.Analytical.OpenStudio`.

Unchanged: all existing members and signatures, codes, `OpenStudioDiagnostic.ToString()`, statistics, `IsValid`, converters, runner, `Grasshopper/` (GUIDs, versions, inputs, outputs, balloons), the benchmark project, every engineering result.

## Decisions and assumptions
- **No new Grasshopper input** (owner decision in the planning session): the legacy `diagnostics` outputs are unchanged, so filtering exists only on the library API. No `diagnosticDetail_`, no version bumps.
- **No new canvas balloons** on the two components that raise none (owner decision). Recorded as a follow-up.
- Message is carried verbatim; `Diagnostic.ToString()` (`Severity: Subject: Message`) is a different format from the legacy text and is not used for the legacy outputs. The record keeps the legacy text.
- Subject = SAM object type + Guid, else the OpenStudio object name, else null. `SourceIndex` = position in the source list (null entries are skipped but still counted).
- Info detail, by code only: Summary = run and workflow outcomes (`SAM-OS-RUN-001`, `SAM-OS-SET-001`, `SAM-OSI-OSM-002`, `OSW-007`, `OSW-008`, `OSW-009`, `SET-001`); Detailed = per-object deferrals and clean-ups (`SAM-OS-GEO-002`, `MAT-002`, `CON-002`, `IC-001`, `SAM-OSI-GEO-002`); everything else, including future codes, Standard. Warnings and Errors are never dropped. This table is an initial classification the owner may want to tune.
- `FormatDiagnostics` skips null entries (the legacy loops would throw on a null).
- Not covered: the string-only producers `Create.DesignDays` and `Modify.AddResults` (no producer severity; their two legacy components keep their Warning mapping).

## Files changed
New: `SAM_OpenStudio/SAM.Core.OpenStudio/Classes/OpenStudioDiagnosticRecord.cs`; `SAM_OpenStudio/SAM.Core.OpenStudio/Query/{ToDiagnosticSeverity,GetDiagnosticDetail,ToDiagnostic,CreateDiagnosticRecords,FilterDiagnosticRecords,ToDiagnostics,FormatDiagnostics}.cs`; `tests/SAM.Analytical.OpenStudio.Tests/D1/DiagnosticsAdapterTests.cs` (20 tests); this record.
Modified: `SAM_OpenStudio/SAM.Analytical.OpenStudio/Classes/{OpenStudioConversionResult,OpenStudioImportResult}.cs` (one added method each).
`git diff --stat -- Grasshopper benchmark` is empty.

## Validation (local Windows, machine-local; not hosted CI)
Windows 11, .NET SDK 10.0.401 (tests target net8.0), SAM built from `sow/2026-Q4` (`SAM\build\SAM.Core.dll` contains the Diagnostics types). `openstudio.exe` **not installed**.
Commands: `dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64`, then `dotnet test <same project> -c Release -p:Platform=x64 --no-build` and `dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/... -c Release -p:Platform=x64 --filter "TestCategory!=Simulation"`.

| Check | Base `fb21e03` | Head `d32658c` |
|---|---|---|
| Build (tests + 3 library projects), Release x64 | 0 errors | 0 errors |
| Functional suite | 328 passed, 24 failed, 2 skipped (354) | **348 passed, 24 failed, 2 skipped (374)** = 328 + 20 new |
| Failing set | 24 tests | **identical 24** (names diffed) |
| Benchmark producer tests | not run on base | 42 / 42 |
| New `DiagnosticsAdapterTests` | - | 20 / 20 |

The 24 failures are the same on base and head and are all simulation tests that need `openstudio.exe` (`CLI required`, `The run must succeed`, `openstudio.exe was not found`); they fail rather than skip when the CLI is absent. The 2 skips are the optional SQL fixture. Hosted CI builds the solution and runs only the benchmark producer tests, not the functional suite.

What the 20 new tests establish: severity maps one to one and an undefined value is Error; code, message, subject and index preserved, original not modified; Warnings and Errors are the same instances in the same order at every level for every code of both code classes (and an unknown future code); Info grows monotonically and each level is an order-preserving subsequence of the next; **Detailed equals the legacy `ToString()` list in text and order**; severity is never taken from text; null / empty inputs; golden legacy text; public-API reflection (constructor, read-only properties, enum names and values, `Diagnostics` property types, `AddDiagnostic` overloads, 17 / 31 code constants); real fixture conversions (clean, missing profiles, two boxes, degenerate panel): the diagnostics, `IsValid` and statistics are unchanged by calling the adapters and match the structured counts; **the saved OSM of the same model is identical (handles normalised) whether or not the adapters run**; failure scenarios without the CLI: missing adjacency cluster, conversion Errors preventing the run (Warning "Simulation not executed" kept with the Error), missing output directory, missing input file, missing import path.
Mutation checks (temporary edits, restored): Warning mapped to Info, filter dropping Warnings, an out-of-range detail level, undefined severity mapped to Info: caught by 4, 5, 3 and 1 failing tests.

## Not run / limitations
- Tests that need `openstudio.exe` / EnergyPlus (24, listed above) and any real simulation: **require a Windows machine with the OpenStudio CLI**. Therefore numerical results of a real run (`heating`, `cooling`, SQL) were not compared base vs head here; the change does not touch the runner, converters or result extraction (diff above), and the OSM identity test covers the exported model.
- The Grasshopper projects were **not built locally** (their post-build step deploys into `%APPDATA%\SAM`); their source is unchanged, hosted CI builds them. No interactive Rhino / Grasshopper check.
- The adapters are not consumed by any component yet; nothing reads the Info detail table in production.
- Detail table is a first classification; no canvas shows it.

## Windows acceptance (exact requirements)
1. SAM `sow/2026-Q4` built Release (`SAM\build\SAM.Core.dll` contains `SAM.Core.Diagnostic`); `dotnet build SAM_OpenStudio.sln -c Release -p:Platform=x64` (this also builds both GH projects and deploys to `%APPDATA%\SAM`, as before).
2. With `openstudio.exe` installed (PATH or standard location): `dotnet test tests/SAM.Analytical.OpenStudio.Tests -c Release -p:Platform=x64`. Expect 374 tests, only the 2 SQL-fixture skips; compare against base `fb21e03`; any failure must also fail identically on base.
3. In Grasshopper (Rhino 8, metre document), on base and on this branch with the same model and EPW: run `SAMAnalyticalToOpenStudio` with `_run` true; `osmPath`, `oswPath`, `sqlPath`, `heating`, `cooling` and `successful` identical, and `diagnostics` byte-identical (text and order). Repeat `OpenStudioRunModel` and `OpenStudioToSAMAnalytical` (OSM and OSW).
4. Open a saved definition containing the three components: loads, wires intact, no "update component" mark (no version change).
5. Optional library check: `result.GetDiagnostics(DiagnosticDetail.Summary)` still holds every Warning and Error of a real run, and `result.Diagnostics.FormatDiagnostics()` equals the `diagnostics` output.

## Next step
Owner review of the draft PR; Windows acceptance (step 2 and 3 need the OpenStudio CLI). After merge: `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4` (docs-only, `[skip ci]`). Follow-ups, each its own PR and owner decision: canvas Warning/Error balloons for `SAMAnalyticalToOpenStudio` and `OpenStudioRunModel`; optional hidden `diagnosticDetail_`; Phase 4B (TAS). Recommended for the next piece: Opus, high effort, new session, Plan mode.
