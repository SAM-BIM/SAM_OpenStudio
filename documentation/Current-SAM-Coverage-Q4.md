# Current SAM analytical coverage and unapplied compliance inputs

Branch: `fix/current-sam-coverage-q4`, from `sow/2026-Q4` at `b43c745f0135e24aa98a6fc3ef02b31e61be6442`. Separate non-icon fix; owner approval required before merge. SAM-BIM/SAM_OpenStudio#24 remains open until this fix is approved/incorporated and its final full-suite validation passes.

## Problem and decision

The current SAM dependency has 16 live enum members missing from the enforced coverage manifest. The unchanged icon head with a valid CLI produces 349 passed / 1 failed / 2 skipped. The remaining failure is `Manifest_CoversEveryLiveEnumMember`.

Audited SAM enum/type documentation and the real conversion pipeline rather than assigning labels to silence the guard. The Ideal Loads translator consumes materialised geometry, internal conditions and schedules; it does not perform Part F airflow or Part O scenario/strategy/equipment preparation. Seven present design inputs now produce explicit `SAM-OS-SET-002` warnings with the original object identity. Four are Unsupported (extract method, door transfer data, scenarios, dwelling strategies); three are Deferred (catalogue equipment selection, project test unit, manual equipment selection). Nine semantic, commissioning or run-audit properties are NA, with individual reasons in the manifest; already-materialised topology/boundary conditions remain authoritative. Measured commissioning rates do not overwrite design rates.

No physical mappings, component GUIDs, existing API signatures, namespaces, icon assets or existing tests were changed. The new diagnostic constant is additive. Present unsupported design inputs remain warnings, consistent with the existing unsupported-data contract; they do not invalidate an otherwise valid conversion or imply compliance approval.

## Files changed

- `Convert/ToOpenStudio/ComplianceInputs.cs`: report unapplied inputs with source identity.
- `Convert/ToOpenStudio/AnalyticalModel.cs`: call reporting on the cloned source before conversion.
- `SAM.Core.OpenStudio/Variables/OpenStudioDiagnosticCodes.cs`: additive diagnostic code.
- `tests/resources/openstudio-analytical-coverage.json`: 16 audited current enum entries (293 total).
- `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md`: matching rows/counts and Q4 audit explanation.
- `tests/SAM.Analytical.OpenStudio.Tests/C7/ComplianceInputTests.cs`: populated/absent cases, warning identity, retained thermal zone and Ideal Loads system.
- This PR record.

## Validation — 2026-10-11

- Full Release x64 solution Restore,Build: PASS (existing dependency warnings remain).
- Complete functional suite: **352 passed / 0 failed / 2 skipped**, total 354. Includes all original 352 cases plus two new tests. TRX: parent `.icon-migration/SAM_OpenStudio/environment/coverage-full.trx`.
- Complete benchmark suite: **41 passed / 0 failed / 0 skipped**. The prior CLI-dependent consumption-units skip now executes and passes.
- Focused completeness/diagnostic suite: **22/22**. Removing the reporting call temporarily makes the populated-input regression fail (1 failed / 1 passed); original source bytes restored and focused regression **2/2** passes.
- `git diff --check`: PASS. No icon/GUID files changed on this fix branch.
- Legitimate existing optional SQL-fixture skips: `RealSqlFixture_SpaceAndSurfaceResults_NoThrow` and `CreateDesignDays_RealSqlReproductionFixture_NoThrow`; fixture not installed (`SAM_OPENSTUDIO_TEST_SQL` or `tests/resources/sql/eplusout.sql`). No test/skip condition was changed.

## CLI environment

Use the official portable [OpenStudio 3.10.0 Windows SDK](https://github.com/NatLabRockies/OpenStudio/releases/expanded_assets/v3.10.0), matching the project's OpenStudio NuGet 3.10.0. Archive `OpenStudio-3.10.0+86d7e215a1-Windows.tar.gz`, SHA256 `4652dae9086a2ad3716dd2a00227c36cb5ed3d2198d0b438f2c0cdd08900e8ba`; CLI reports `3.10.0+86d7e215a1`. Extract into a writable local runtime directory and prepend its `bin` to the test process PATH. EnergyPlus is bundled. No system-wide installation or PATH/ACL change required.

```powershell
$env:PATH = 'C:/your/runtime/OpenStudio-3.10.0+86d7e215a1-Windows/bin;' + $env:PATH
dotnet test tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release -p:Platform=x64
dotnet test benchmark/SAM.Analytical.OpenStudio.Benchmark.Tests/SAM.Analytical.OpenStudio.Benchmark.Tests.csproj -c Release -p:Platform=x64
```

## Remaining and exact next step

Final-head build/SPDX CI must pass and owner must approve this separate non-icon fix before merge. After approved merge, update existing icon PR24 from Q4 (normal merge; no force-push), rerun its complete x64 functional/benchmark suites, solution build and icon/GUID/resource guards, and verify exact-head CI. Only then perform the authorised icon merge and Q4 docs-only `[skip ci]` closeouts with known merge SHAs. Runtime/TRX files are local evidence; provision/copy them on another machine.
