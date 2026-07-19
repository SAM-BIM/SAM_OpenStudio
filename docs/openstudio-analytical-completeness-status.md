# SAM → OpenStudio Analytical Completeness v1 — Status

Programme: complete every non-HVAC part of `SAM.Analytical.AnalyticalModel` translation with a
defensible OpenStudio/EnergyPlus representation — native where possible, controlled approximation
where necessary, structured diagnostic where unsupported, never silently dropped. Ideal Loads
remains the simulation system. Branch: `feature/openstudio-analytical-completeness` (base
`sow/2026-Q3`, MVP baseline `bf75bf3`, 82/82 tests).

| Milestone | Scope | Status | Commit |
|---|---|---|---|
| C0 | Complete SAM analytical coverage audit (Markdown + machine-readable manifest) | **Done** | — |
| C1 | Core adapter hardening (P3 items, disposal, statistics, SQL, VersionTranslator) | Pending | — |
| C2 | Internal conditions, schedules, conditioning modes (latent, humidistat, single-mode) | Pending | — |
| C3 | Fenestration, frames, constructions (FrameAndDivider, doors, hole diagnostics) | Pending | — |
| C4 | Site, weather, DDY, simulation settings (north, ground temps, run period, calendar) | Pending | — |
| C5 | Results extraction and SAM result mapping (engine-neutral schema, units authority) | Pending | — |
| C6 | Cancellation, asynchronous execution, Grasshopper UX | Pending | — |
| C7 | Full regression suite, completeness enforcement, documentation | Pending | — |

## C0 — Coverage audit (done)

- `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md` — full audit of the SAM analytical data surface;
  tables generated from the manifest.
- `tests/resources/openstudio-analytical-coverage.json` — machine-readable manifest (279 entries)
  consumed by the C7 completeness tests (tests never parse the Markdown).
- Baseline verified on this machine before work started: x64 Debug build clean,
  **82/82 tests passing** (including the EnergyPlus simulation tests), OpenStudio CLI
  3.10.0+86d7e215a1, OpenStudio NuGet 3.10.0 restored.
- Classification counts at C0: Native 135, Derived 28, Approximated 21, Unsupported 20,
  Deferred 17, NA 58 across 279 entries (MVP already covers 90).
- Agreed reclassifications (binding): dividers → NA (no SAM source data); blinds/shades and
  opening properties → Unsupported with diagnostics; STAT parsing → deferred; SAM hourly design
  days → Approximated parametric fit (DDY import is the deterministic primary path); subhourly
  profiles → NA (SAM profiles are hour-indexed); daylight saving → option, default off; emitter
  radiant proportions → Deferred; pollutant → Unsupported.

## Gates log

| Milestone | Build | Tests | EnergyPlus gate |
|---|---|---|---|
| Baseline | clean (0 errors) | 82/82 | SingleBox/AirGap/TwoBoxes end-to-end + CLI timeout kill |
| C0 | clean (docs only) | 82/82 | n/a (no code change) |
