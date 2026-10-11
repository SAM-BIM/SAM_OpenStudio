# Icon migration validation — 2026-10-11

Base: `sow/2026-Q4`. Scope: SAM-BIM only; existing icon PR [#24](https://github.com/SAM-BIM/SAM_OpenStudio/pull/24). Approved icon design and component GUIDs/APIs/namespaces/behaviour preserved.

## Resolved blockers

- Matching official portable OpenStudio 3.10.0 CLI (including EnergyPlus), archive SHA256 `4652dae9086a2ad3716dd2a00227c36cb5ed3d2198d0b438f2c0cdd08900e8ba`, supplied through process-local PATH; no system installation or ACL changes. See `Current-SAM-Coverage-Q4.md` for reproducible commands.
- Separate non-icon fix [#25](https://github.com/SAM-BIM/SAM_OpenStudio/pull/25), owner approved after green exact-head build/SPDX, merged as `68acc6a74aafb8f097e74e9795d91a2f05fda80f`. Audits 16 current enum members and emits real source-identified warnings for seven unapplied compliance inputs. Nine metadata/commissioning/audit fields have individual NA reasons. Coverage 293 entries; physical mappings unchanged. Regression mutation caught missing reporting; source restored.
- Normal Q4 merges incorporated the approved fix and its docs closeout; no force-push or Q3 changes. Full-suite passage remains required, per owner decision; baseline failures alone did not authorise merge.

## Final validation

Production/test tree tested at `8f602f90c2db188a62a2ec8d23976191a9c3b47f`; subsequent evidence edit is documentation only.

- Full VS MSBuild solution Restore,Build Release **x64 PASS** with isolated APPDATA/USERPROFILE and current SAM-BIM sibling dependencies.
- Complete functional suite: **352 passed / 0 failed / 2 existing skips**, total 354 (`icon-final-functional.trx`).
- Complete benchmark suite: **41 passed / 0 failed / 0 skipped** (`icon-final-benchmark.trx`); former CLI-dependent skip now executes.
- Only existing optional SQL-fixture skips: `RealSqlFixture_SpaceAndSurfaceResults_NoThrow` and `CreateDesignDays_RealSqlReproductionFixture_NoThrow`. Install a real fixture through `SAM_OPENSTUDIO_TEST_SQL` or `tests/resources/sql/eplusout.sql` to execute them. No existing test or skip condition changed.
- Icon source guard against current Q4: **6 resource-token swaps / zero unrelated production C# changes**. All 6 concrete manifest objects and resource references pass; GUIDs unchanged; canonical 24x24 RGBA assets and embedded PNG resources pass (6 required names present, zero legacy resources).
- `git diff --check`: PASS. Fresh hosted Rhino smoke is not claimed.

Local build/log/TRX evidence: parent `.icon-migration/SAM_OpenStudio/environment/`. Provision/copy the portable runtime and evidence on another machine; Git holds code and reproduction instructions, not local binaries.

## Exact next step

Push this existing icon branch normally, verify build/SPDX at the exact final PR head, then perform the authorised icon merge only if both pass. After the merge SHA is known, add a docs-only `[skip ci]` PROJECT_PROGRESS closeout directly on Q4. Preserve branches, frozen Q3 and unrelated optimisation PRs.