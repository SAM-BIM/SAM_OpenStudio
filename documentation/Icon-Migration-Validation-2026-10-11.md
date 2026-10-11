# Icon migration validation — 2026-10-11

Base: `sow/2026-Q4`. Scope: SAM-BIM only. Reference: [SAM#179](https://github.com/SAM-BIM/SAM/pull/179); [SAM#166](https://github.com/SAM-BIM/SAM/pull/166) is the closed historical design source.

## Changes and decisions

- Grasshopper manifest coverage: 6 concrete objects. GUIDs, APIs, namespaces, component metadata and behavior are preserved.
- Reuse the frozen SAM glyph/palette/badge grammar and frozen SAM-BIM extension v1. Preserve plugin identity, external integration logos, severity symbols and historical attribution.
- Source comparison against current base: 6 icon resource token swaps; 0 other production C# changes, apart from standard SPDX headers and additive resource properties.
- Validated 6 manifest resource references; 0 errors.

## Fresh validation

- VS MSBuild Restore,Build, Release: **PASS**. Scratch APPDATA/USERPROFILE; current sibling SAM-BIM dependencies, OCCT native build skipped.
- Regression suites: benchmark.trx: Completed; passed 40, failed 0, total 41; functional.trx: Failed; passed 325, failed 25, total 352
- Re-parsed compiled component declarations: 6/6 manifest rows; 0 coverage/canonical 24x24 RGBA PNG errors.
- Built assembly resource presence and exact embedded PNG bytes checked.

## Blockers — do not merge

Fresh x64 functional suite: 325 passed, 25 failed, 2 skipped. Most failures require missing OpenStudio/EnergyPlus CLI; Manifest_CoversEveryLiveEnumMember also reports uncovered current SAM SpaceParameter members (SpaceSemantics, SpaceUseOverride, PartFLocalExtractMethod). Benchmark: 40 passed, 1 skipped. Resolve environment/current dependency baseline separately.

Next step: resolve any listed blockers, then verify final-head GitHub checks and merge only if green and safe under the owner authorization. No force-push or branch deletion. Hosted Rhino smoke is not claimed as a fresh pass; source and embedded-resource checks are the completed icon verification.
