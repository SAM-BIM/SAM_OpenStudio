# OpenStudio MVP — M0 toolchain audit

**Date:** 2026-07-18
**Branch:** `feature/analytical-model-to-openstudio-mvp` (off `sow/2026-Q3` @ 94dfce9)
**Plan:** [SAM_OpenStudio_MVP_Implementation_Plan.md](SAM_OpenStudio_MVP_Implementation_Plan.md)

## 1. SDK / CLI version gate — DECISION: OpenStudio 3.10.0

| Fact | Value |
| --- | --- |
| NuGet versions available | …, 3.7.0, 3.8.0, 3.10.0, 3.11.0 (no 3.9.0) |
| Previous SDK reference | 3.8.0 (all three library projects) |
| Installed CLI + EnergyPlus | **3.10.0+86d7e215a1** at `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` |
| Action taken | Bumped NuGet `OpenStudio` 3.8.0 → **3.10.0** in `SAM.Core.OpenStudio`, `SAM.Geometry.OpenStudio`, `SAM.Analytical.OpenStudio` |
| Result | Full solution rebuild: **0 errors**; existing SQL-results code compiles unchanged; smoke tests pass |

Rationale: exact SDK/CLI match removes OSM version translation from the production path
(the pre-existing `Create.Model` uses `Model.load`, which does **not** version-translate).
The CLI smoke test still exercises `OSVersion::VersionTranslator` for robustness.

## 2. Toolchain environment (this machine)

- .NET SDK 10.0.302; runtimes include .NET 8.0.14 → net8.0 test project runs natively.
- SAM prebuilt assemblies present at `..\SAM\build\` (SAM.Core/Geometry/Analytical/Weather) — consumed via existing HintPath convention; sibling repos untouched.
- Rhino 8 installed (`C:\Program Files\Rhino 8`) → the mandatory Stage-5 Rhino smoke test is feasible on this machine.
- **Platform constraint:** the OpenStudio NuGet `build\netstandard2.0\OpenStudio.targets` raises an *error* unless `$(Platform)` is `x64` or `x86`. All builds/tests must pass `-p:Platform=x64` (the test project also defaults its own Platform to x64, mirroring the Grasshopper projects).
- Package layout: managed `OpenStudio.dll` + native `openstudiolib.dll`, `openstudio_csharp.dll`, `openstudio_model_csharp.dll`, `openstudio_translators_csharp.dll` under `build\netstandard2.0\x64\`, injected as Reference + copied as Content → natives flow transitively to test and Grasshopper outputs.

## 3. M0 smoke tests (both green)

`tests\SAM.Analytical.OpenStudio.Tests\M0\OpenStudioSmokeTests.cs`, run with
`dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Debug -p:Platform=x64`:

1. `OpenStudio_CreateSaveReload_RoundTrips` — creates `Model` + `Space` + `ThermalZone`, saves OSM, reloads via the existing `SAM.Core.OpenStudio.Create.Model`, asserts counts and preserved names. Proves managed + native x64 bindings under net8.0 outside Rhino.
2. `OpenStudioCli_Discovered_And_OpensGeneratedOsm` — discovers the CLI via the new `Query.OpenStudioCliPath` (explicit → PATH → direct installs → ladybug_tools), checks `openstudio_version`, then has the CLI load the SDK-generated OSM through `OSVersion::VersionTranslator` in a ruby script (`execute_ruby_script`) and verify the space count. Proves SDK↔CLI interop.

## 4. Existing code audit (keep / reuse map)

| Area | Files | Verdict |
| --- | --- | --- |
| SQL result reading (`SAM.Core.OpenStudio`) | `Create\SqlFile.cs`, `Create\Model.cs`, `Create\ShortDateTime.cs`, `Classes\ShortDateTime.cs`, `Convert\ToSystem\*.cs`, `Enums\ReportingFrequency.cs`, `Query\ReportData*.cs`, `Query\AvailableTimeSeriesNames.cs`, `Query\AvailableEnviromentPeriodsNames.cs`, `Query\EnvironmentPeriodIndex.cs`, `Query\TimeIndex*.cs`, `Query\DateTime.cs`, `Query\SurfaceIndexes.cs`, `Query\SurfaceNames.cs`, `Query\ConvertUnit.cs`, `Query\Units.cs`, `Query\Values.cs`, `Query\Min.cs`, `Query\Max.cs` | **Keep — reuse in M6** load extraction |
| Results import (`SAM.Analytical.OpenStudio`) | `Convert\ToSAM\SpaceSimulationResults.cs`, `Convert\ToSAM\PanelSimulationResults.cs`, `Create\SpaceSimulationResults.cs`, `Create\PanelSimulationResults.cs`, `Create\DesignDays.cs`, `Query\DesignDays.cs`, `Query\PanelSimulationResults.cs`, `Query\Source.cs`, `Modify\AddResults.cs`, `Enums\Parameter\*.cs` | **Keep — reuse in M6** |
| Geometry stub | `SAM.Geometry.OpenStudio\Classes\Test.cs` | **Removed** (empty placeholder; real content lands in M2) |
| Grasshopper | `OpenStudioCreateDesignDaysBySQL`, `OpenStudioCreateSpaceSimulationResultsBySQL`, `SAMAnalyticalAddResultsBySQL` | **Keep** (modernised upstream in 94dfce9); new converter components land in M8 |

Improvement noted, deliberately not changed now: `Create.Model` loads OSM without
`VersionTranslator`; acceptable while SDK == CLI == 3.10.0. The M6 runner will use
`VersionTranslator` where it loads foreign OSM files.

## 5. Weather fixture (pinned)

`tests\resources\weather\` — Boston Logan Intl AP (WMO 725090), TMYx 2004-2018,
`.epw` + `.ddy` + `.stat`, SHA-256 in `SHA256SUMS.txt`, provenance and licence in the
folder `README.md`. Committed (no downloads at test time). Boston chosen over a mild UK
climate so the conditioned fixture produces non-zero annual heating **and** cooling —
required by the M6 gate.

## 6. Known warnings / accepted debt

- `MSB3277` System.Memory 4.0.1.2 vs 4.0.2.0 unification warning in
  `SAM.Analytical.OpenStudio` — **pre-existing on the 3.8.0 baseline**, unchanged by
  3.10.0; originates from `SAM\build\NetTopologySuite.dll` vs the System.Memory 4.5.5
  package; benign for a netstandard2.0 library (final unification happens in the
  consuming app). Out of MVP scope.
- Repository has no `.gitattributes`; git reports LF→CRLF normalisation notices on new
  files. Cosmetic.
