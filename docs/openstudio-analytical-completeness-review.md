# SAM → OpenStudio Analytical Completeness v1 — independent review

**Reviewer:** independent senior C# building-performance engineer (correction engineer for confirmed findings)
**Date:** 2026-07-20
**Branch:** `feature/openstudio-analytical-completeness` (base `sow/2026-Q3` @ `bf75bf3`, merged MVP)
**Milestones under review:** C0 `b138a65` … C7 `d1fc7fd` (eight commits, inspected individually)
**Method:** every claim in [openstudio-analytical-completeness-status.md](openstudio-analytical-completeness-status.md)
treated as unverified until reproduced. All changed production and test files read in full; the
279-entry coverage manifest re-counted and cross-checked against the Markdown document and the
live SAM enums; SAM sibling sources (READ-ONLY) consulted for units, load-query semantics and
the TAS north-angle convention; five complete EnergyPlus simulation scenarios (eight CLI runs)
reproduced independently of the test suite through a standalone console driver.

---

## 1. Executive summary

The programme substantially delivers what it claims: the build and the 146/146 suite reproduce
exactly, the coverage manifest counts are correct and in lockstep with the Markdown document,
the fenestration/frame geometry is numerically exact against EnergyPlus output, the annual
environment-period filter provably excludes sizing days, north rotation behaves per the
SAM_Tas convention, the humidistat wiring has a material and physically correct effect on
results, and the cancellation/process-tree-kill machinery is real.

Three P1 findings were confirmed by reproduction — all three in the newest (C2–C5) layers:

| ID | Priority | Finding | Status |
| --- | --- | --- | --- |
| P1-01 | P1 | Eighteen coverage-manifest rows declare structured diagnostics that no converter ever emits (false completeness: view coefficients, lighting control function, internal-shadow flags, panel feature shades, vapour diffusion factor, opaque internal-optics divergence, emitter and exhaust parameters) | **Fixed** (Stage L, §8) |
| P1-02 | P1 | The default DDY design-day filter (`"99.6%"`/`"0.4%"`) imports **no cooling design day** (ASHRAE DDYs name them `Ann Clg .4% …`) and wrongly imports humidification (`Hum_n`) and wind (`Htg Wind`) 99.6% days | **Fixed** (Stage L, §8) |
| P1-03 | P1 | Leap-year runs: SQL hour-of-year uses a fixed non-leap reference year, so Feb 29 rows clamp onto Feb 28 (duplicate hour keys double-count the coincident peak) and all post-February peak hours shift by one day | **Fixed** (Stage L, §8) |
| P2-01 | P2 | The C7 completeness test does not implement the stale-manifest-id detection the coverage document claims | **Fixed** (Stage L, §8) — the new check immediately caught 2 real stale rows |
| P2-02 | P2 | `OutputVariableFrequency` ≠ Hourly silently mis-scales extracted peaks (fixed 3600 s interval assumption) | **Mitigated** (Stage L, §8); frequency-aware extraction stays follow-up |
| P2-03 | P2 | SAM `Location` site override writes non-finite/out-of-range coordinates into `OS:Site` unvalidated | **Fixed** (Stage L, §8) |
| P2-04 | P2 | Standalone `Run(path, …)` ignores `UseUniqueRunDirectory` for `.osw` inputs and has no collision lock on that path | **Open (follow-up)** — needs an OSW-rewrite design; not fixed here |
| P2-05 | P2 | Grasshopper: removing the component or closing the document does not cancel a running simulation; the completion callback can target a disposed document | **Fixed** (Stage L, §8) — compile-verified; runtime on the human Rhino checklist (§10 step 5) |
| P2-06 | P2 | Encoding corruption: coverage Markdown carries double-encoded arrows (`â†’`); the JSON manifest carries a raw CP1252 byte (invalid UTF-8) | Confirmed — fix pending |

No P0 finding exists: nothing crashes, corrupts an *annual* result in the shipped default
configuration, mutates sources, leaks processes or endangers the repository. P1-02 and P1-03
corrupt *sizing/peak* semantics only under the design-day and leap-year options respectively;
P1-01 breaks the programme's own "nothing silently dropped" contract.

## 2. Reproduced baseline (Stage B)

| Item | Claimed | Reproduced |
| --- | --- | --- |
| Restore | — | `dotnet restore SAM_OpenStudio.sln` — up to date |
| Build | clean, 0 errors | `dotnet build -c Debug -p:Platform=x64 -m:1` — **0 errors**, only the pre-existing benign MSB3277 System.Memory warning (×2 projects) |
| Tests | 146/146 | **146/146 passed, 0 skipped**, duration 1 m 28 s (89.2 s wall clock) |
| Simulation tests | ~30 E+ runs | 20 `[Category("Simulation")]` test methods, several multi-run — consistent with ~30 CLI executions; suite duration (~90 s vs ~20 s conversion-only) confirms they executed |
| OpenStudio SDK | NuGet 3.10.0 | confirmed in all three library csproj files |
| OpenStudio CLI | 3.10.0+86d7e215a1 | `openstudio openstudio_version` at `C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe` |
| EnergyPlus | — | **25.1.0-1c11a3d85f** (bundled) |
| Coverage manifest | 279 entries; N135/D28/A21/U20/Def17/NA58 | **Reproduced exactly** (script count); 0 duplicate ids; MD ↔ JSON ids identical (279 ↔ 279, no drift in either direction). *Stage L correction: 2 of the 279 turned out to be stale (P2-01) — final manifest 277 entries, NA 56* |
| Sibling repositories | untouched | `git status` clean in SAM, SAM_LadybugTools, SAM_UI, SAM_SQLite, SAM_Tas |
| Committed artifacts | none | diff contains only source/docs/tests; no binaries, run outputs or machine paths |

### Independent simulation reproduction (standalone driver, outside the test suite)

Eight CLI runs across five scenarios; every run: exit 0, 0 fatal, 0 severe, SQL present,
result-zone count = conditioned-zone count.

| # | Scenario / fixture | Env. periods | Zones | Heating kWh | Cooling kWh | Peak H / C kW (@h) | Runtime | Semantic evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Latent + humidistat (SingleBox, latent 5 W/m², RH 40/60%) | 1 | 1/1 | 3832.40 | 1844.13 | 2.493 @702 / 2.145 @5917 | 4.5 s | Min zone RH 2.6 % (uncontrolled) → **29.1 %**; max capped at **60.5 %** by the 60 % dehumidification setpoint; heating +907 kWh from humidification/latent — humidity control materially and correctly affects outputs. Min RH sits below the 40 % setpoint only in zero-load dead-band hours (Ideal Loads delivers no carrier air) — correct EnergyPlus physics |
| 2 | Framed window (SingleBox, 50 mm PVC frame) | 1 | 1/1 | 2948.18 | 1670.62 | 2.015 @702 / 2.103 @5533 | 3.0 s | SAM aperture 2.80 m² = E+ glass **2.470** + E+ frame **0.330** m² (EnvelopeSummary); pane polygon in IDF; FrameAndDivider present |
| 3 | DDY design days + sizing (SingleBox) | 4 (3 sizing + annual) | 1/1 | 2924.81 | 1758.03 | 2.025 @702 / 2.173 @5532 | 3.0 s | Annual totals **identical to baseline to the shown precision** (2924.81/1758.03 both runs) — environment filter proven; ZoneSizes populated. Pre-fix evidence for P1-02: imported days were `ANN HTG 99.6% CONDNS DB`, `ANN HTG WIND 99.6%`, `ANN HUM_N 99.6%` — **no cooling day** |
| 4 | North rotation 0° vs 180° (SingleBox, south window) | 1 | 1/1 | 2924.81 → 3407.03 | 1758.03 → 1380.72 | — | 2.9 s | Transmitted window solar 1316.8 → **436.4 kWh**; heating up, cooling down — orientation/sign convention proven end-to-end (identical transform to SAM_Tas `ToT3D/Building.cs`: radians → degrees, no inversion) |
| 5 | Rich extraction (TwoAdjacentBoxes, series on) | 1 | 2/2 | 5532.11 | 2890.57 | 3.633 @702 / 3.457 @4192 | 4.3 s | Per-zone energies/peaks for both zones; unmet hours and gains normalised onto the energy zone identity; enclosure-scoped window solar extracted; 8760-value temperature/operative/humidity series per zone; coincident peak ≤ Σ zone peaks |

Additional probe: a **framed internal (interzone) window** (TwoAdjacentBoxes, shared-wall
window, all apertures framed) runs cleanly — 3 FrameAndDivider objects reach the IDF, exit 0,
0 severe, no frame-related err entries. EnergyPlus 25.1 accepts interzone frames; no finding.

## 3. P0 findings

None.

## 4. P1 findings

### P1-01 — Declared coverage diagnostics never emitted (false completeness)

- **Priority:** P1 (silent omission; violates the programme's core "never silently dropped" contract)
- **Summary:** 18 manifest rows declare a structured diagnostic that no production code path
  ever emits. The converter never reads these parameters at all, so data present on real models
  is dropped with no trace — exactly the "false completeness" failure mode this review was
  directed to find. The C7 enforcement test only proves the pollutant diagnostic.
- **Affected entries and files:**
  - `InternalConditionParameter.OccupancyViewCoefficient`, `.EquipmentViewCoefficient`,
    `.LightingControlFunction` (Unsupported, SAM-OS-IC-001) — never referenced in
    `Convert/ToOpenStudio/InternalCondition.cs`;
  - `InternalConditionParameter.HeatingEmitterRadiantProportion`, `.HeatingEmitterCoefficient`,
    `.CoolingEmitterRadiantProportion`, `.CoolingEmitterCoefficient`,
    `.ExhaustAirFlowPerPerson`, `.ExhaustAirChangesPerHour`, `.ExhaustAirFlowPerArea`,
    `.ExhaustAirFlow` (Deferred, declared SAM-OS-HVAC-001 info) — never referenced;
  - `ApertureConstructionParameter.IsInternalShadow`, `ConstructionParameter.IsInternalShadow`
    (Unsupported, SAM-OS-CON-002) — never referenced (`Convert/ToOpenStudio/Aperture.cs`,
    `Construction.cs`);
  - `PanelParameter.FeatureShade` (Unsupported, SAM-OS-CON-002) — only the
    *ApertureParameter* FeatureShade is checked (`Aperture.cs:282`); the panel-level flag is not
    (`Panel.cs`);
  - `AnalyticalMaterialParameter.VapourDiffusionFactor` (Unsupported, SAM-OS-MAT-002) — never
    referenced (`Material.cs`);
  - `OpaqueMaterialParameter.InternalEmissivity`, `.InternalLightReflectance`,
    `.InternalSolarReflectance` (Approximated, "SAM-OS-MAT-002 info when Internal differs from
    External") — the opaque `Internal*` parameters are never read (`Material.cs` reads only the
    transparent-material `Internal*` set), so the declared divergence diagnostic can never fire.
- **Reproduction:** `grep -rE "OccupancyViewCoefficient|EquipmentViewCoefficient|LightingControlFunction|ExhaustAir|Emitter|IsInternalShadow|VapourDiffusionFactor" SAM_OpenStudio/` —
  no production hits (only the manifest/docs). Convert a model whose internal condition carries
  `OccupancyViewCoefficient` (SAM's own `Create.InternalCondition` sets 0.227–0.372 on every
  NCM-derived condition) → zero diagnostics referencing it.
- **Observed:** parameters silently ignored; `CleanFixture_DropsNothing` passes because the
  fixtures never set them; `UnsupportedData_RaisesExactlyTheDeclaredDiagnostics` tests pollutant only.
- **Expected:** per the manifest scheme — "Unsupported: when source data is present a structured
  diagnostic is emitted. Nothing is silently dropped"; Approximated rows fire their declared
  conditional diagnostic; Deferred rows with a declared diagnostic report the deferral.
- **Impact:** real production models (NCM data, TAS imports with emitter/exhaust definitions,
  materials with vapour-diffusion factors, asymmetric opaque optics) lose data with no trace;
  the coverage report overstates the translation.
- **Correction:** emit the declared diagnostics at the natural conversion sites (internal
  condition → SpaceType creation; aperture/construction/panel/material converters), info/
  warning severity exactly as declared; no conversion behaviour changes otherwise.
- **Regression tests:** one per emission site asserting the declared code fires when the data is
  present, and the clean-fixture test still drops nothing.
- **Status:** **Fixed** (Stage L) — declared diagnostics emitted at all five converter sites
  (InternalCondition: view coefficients, control function, emitter/exhaust deferrals;
  Aperture + Construction: internal-shadow flags; Panel: feature shade; Material: vapour
  diffusion factor and opaque internal-optics divergence, each once per source object via the
  new `OpenStudioConversionContext.RegisterOnce`); the SAM-OS-HVAC-001 summary widened to cover
  the deferral usage (P3-05). Regression suite `C7/DeclaredDiagnosticsTests` (8 tests, incl.
  once-per-object and matching-optics-silence); clean fixture still drops nothing. Commit in §8.

### P1-02 — Default DDY import selects no cooling design day (and wrong 99.6% days)

- **Priority:** P1 (wrong design-day behaviour; silent omission of the cooling sizing day)
- **Summary:** `Convert/ToOpenStudio/DesignDays.cs` filters by
  `name.Contains("99.6%") || name.Contains("0.4%")`. ASHRAE/climate.onebuilding DDY files name
  cooling days `… Ann Clg .4% Condns DB=>MWB` — **without a leading zero** — so no cooling day
  ever matches; and `"99.6%"` also matches the humidification (`Ann Hum_n 99.6%`) and wind
  (`Ann Htg Wind 99.6%`) days the documented policy ("the heating 99.6% / cooling 0.4% pair")
  does not intend. The referenced OpenStudio `baseline_model.rb` filters on
  `Htg 99.6. Condns DB` / `Clg .4. Condns DB=>MWB`.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Convert/ToOpenStudio/DesignDays.cs:41-54`;
  `tests/.../C4/SiteWeatherSettingsTests.cs:99-109` (tautological assertions).
- **Reproduction:** default DDY import of the pinned Boston DDY; SQL
  `EnvironmentPeriods` of the independent run 3 (pre-fix):
  `ANN HTG 99.6% CONDNS DB | ANN HTG WIND 99.6% CONDNS WS=>MCDB | ANN HUM_N 99.6% CONDNS DP=>MCDB | SAM_RUNPERIOD`
  — three heating-type sizing days, zero cooling days.
- **Observed:** cooling sizing (`Sizing:Zone` cooling loads, `ZoneSizes` cooling rows,
  `Result.Space.DesignLoad` for cooling) computed from heating-condition days — meaningless;
  the C4 test passes because `Count ≥ 2` is satisfied by the three spurious matches and the
  second assertion merely re-applies the same filter.
- **Expected:** default selection = the annual heating 99.6% dry-bulb day plus the annual
  cooling .4% (or 0.4%) DB=>MWB day; humidification/wind days excluded; fallback to all days
  (with the existing warning) when the convention matches nothing.
- **Impact:** every default design-day workflow silently lacks cooling sizing.
- **Correction:** regex selection — heating `Ann Htg 99.6% Condns DB` (excluding `Wind`),
  cooling `Ann Clg (0)?\.4% Condns DB=>M(C)?WB`; per-side warning when one side finds no match;
  fallback to all days when neither matches. Rewrite the C4 assertion to name the two days.
- **Regression tests:** default import of the pinned DDY yields exactly one heating
  (`…Ann Htg 99.6% Condns DB`) and one cooling (`…Ann Clg .4% Condns DB=>MWB`) day, and no
  `Hum_n`/`Wind` day; `ImportAllDesignDays` unchanged; end-to-end sizing run shows both a
  heating and a cooling sizing environment.
- **Status:** **Fixed** (Stage L) — regex selection (`Ann Htg 99.6% Condns DB` /
  `Ann Clg 0?.4% Condns DB=>M(C)?WB`, case-insensitive) with per-side warnings and the
  existing all-days fallback when neither side matches. C4 assertion rewritten to name the
  exact pair; new heating-only-DDY warning test; the end-to-end sizing run now asserts (via
  SQL EnvironmentPeriods) exactly one heating and one cooling sizing environment and identical
  annual totals. Commit in §8.

### P1-03 — Leap-year Feb 29 collapses onto Feb 28 in the SQL hour-of-year

- **Priority:** P1 (wrong calendar/result behaviour under the supported `IsLeapYear` option)
- **Summary:** `OpenStudioSimulationRunner.ReadHourlyValueSeries` computes hour-of-year with a
  fixed non-leap reference year (`DaysInMonth(2023, …)`, `new DateTime(2023, …)`). In a
  leap-year run (C4 `IsLeapYear` + leap EPW), EnergyPlus reports Month=2/Day=29 rows: the
  day-clamp maps them onto Feb 28 (duplicate hour indices), and every date after February lands
  one day early. `CoincidentPeak` accumulates per hour index, so Feb 28 + Feb 29 values sum
  into the same 24 buckets — the building-level coincident peak (and the SAM
  `PeakHeating/CoolingLoad`/`PeakHour` written by `ToSAM`) can be inflated up to 2×; per-zone
  peak *hours* after February are off by 24 h relative to the 8784-hour series.
- **Affected files:** `SAM_OpenStudio/SAM.Analytical.OpenStudio/Classes/OpenStudioSimulationRunner.cs:985-988`
  (with `CoincidentPeak` at 835-868 consuming the duplicated keys).
- **Reproduction:** synthetic EnergyPlus-schema SQLite with hourly rows for Feb 28 and
  Feb 29 (leap year): pre-fix, both days produce the same 24 hour indices (1392…1415), and the
  summed "coincident" magnitude doubles. (The pinned Boston TMYx EPW is non-leap, so the shipped
  test suite cannot reach this path — the C4 leap test stops at schedule generation.)
- **Expected:** hour-of-year computed against the run calendar — Feb 29 gets its own 24
  indices, Mar 1 starts at hour 1440 (leap) / 1416 (non-leap), Dec 31 ends at 8783 (leap).
- **Impact:** leap-year simulations (AMY 2020/2024 weather, `IsLeapYear = true`) report corrupted
  coincident peaks and shifted peak timestamps into SAM results.
- **Correction:** detect Feb 29 in the returned rows and use a leap reference year (2024) for
  the day-of-year computation; keep the defensive clamp for genuinely out-of-range values.
- **Regression test:** synthetic SQL (leap dataset) through the internal reader: 48 distinct
  indices across Feb 28/29, Mar 1 hour 0 at index 1440, no duplicate hour keys.
- **Status:** **Fixed** (Stage L) — the reader now selects the reference year from the data
  (Feb 29 present in the read environment → leap year 2024, else 2023; the defensive clamp
  stays for genuinely invalid dates). Regression suite `C5/LeapYearIndexingTests`: leap
  dataset yields 50 distinct indices (Feb 28 @1392, Feb 29 @1416, Mar 1 @1440, Dec 31 @8783);
  a non-leap dataset keeps the original indexing (Mar 1 @1416, Dec 31 @8759). Commit in §8.

## 5. P2 findings

### P2-01 — Stale-manifest-id detection claimed but not implemented

- **Summary:** the coverage document states the completeness test fails when "a manifest entry
  references an enum member that no longer exists (stale coverage)". `Manifest_CoversEveryLiveEnumMember`
  checks only live member → manifest; a manifest id with a covered-enum prefix but a
  non-existent member passes silently, so renames/removals in SAM would leave stale rows.
- **Files:** `tests/.../C7/CompletenessTests.cs:89-120`; `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md` (§Enum completeness enforcement).
- **Correction:** reverse check — every manifest id whose prefix matches a covered enum must
  name a live member.
- **Status:** **Fixed** (Stage L) — `Manifest_HasNoStaleIds` added. On its first run against the
  shipped manifest it caught **two real stale rows**: `AnalyticalMaterialParameter.TypeName`
  and `.Description` name members that are commented out in the live
  `SAM.Analytical.MaterialParameter` enum (the C0 audit transcribed them; this review's own
  Stage D "no stale entries today" verification also missed them — corrected in §7). Both rows
  removed from the JSON manifest and the Markdown document (byte-level edit preserving the
  pre-P2-06 encoding); coverage totals are now **277 entries, NA 56, C0 62** (documents
  updated). Failure mode reproduced with an injected ghost id before the manifest correction.
  Commit in §8.

### P2-02 — Non-hourly OutputVariableFrequency mis-scales extracted peaks

- **Summary:** `OutputVariableFrequency` is honoured for the `Output:Variable` requests, but the
  extractor converts per-row energies with a fixed 3600 s interval (`JoulesPerIntervalToWatts`
  default): "Timestep" under-reports peaks ×(1/timesteps), "Daily" inflates them ×24, and the
  8760-length series assumptions break.
- **Files:** `Classes/OpenStudioSimulationRunner.cs` (FillPeaks/CoincidentPeak);
  `SAM.Core.OpenStudio/Query/ConvertUnit.cs`; `Convert/ToOpenStudio/SimulationSettings.cs:112`.
- **Correction (mitigation, this branch):** a warning diagnostic at conversion when the
  requested frequency is not Hourly, stating peaks/series in the result set assume hourly
  reporting. Full frequency-aware extraction is follow-up work.
- **Status:** **Mitigated** (Stage L) — new `SAM-OS-RUN-003` (ResultExtractionLimitation)
  warning at conversion naming the non-hourly frequency; the request itself is still honoured
  and the default stays silent (both tested). Frequency-aware peak extraction remains a
  follow-up. Commit in §8.

### P2-03 — SAM Location override unvalidated

- **Summary:** `Weather.cs` copies `Location.Latitude/Longitude/Elevation` into `OS:Site`
  whenever a SAM Location exists — including NaN or out-of-range values (a NaN reaches the OSM
  and EnergyPlus). The precedence itself is correct and tested; only validation is missing.
- **Files:** `Convert/ToOpenStudio/Weather.cs:59-66`.
- **Correction:** override only when latitude ∈ [−90, 90], longitude ∈ [−180, 180] and both
  finite (elevation finite, else 0-defaulted by OS); otherwise keep the EPW site and raise a
  warning naming the rejected values.
- **Status:** **Fixed** (Stage L) — invalid coordinates keep the EPW site with a SAM-OS-RUN-002
  warning naming the rejected values; a non-finite elevation keeps the EPW elevation (never a
  NaN into OS:Site) while valid coordinates still override, and the override message says so.
  Regression tests: NaN latitude, longitude 200 and NaN elevation (C4). Commit in §8.

### P2-04 — Standalone `Run(path, …)` collision behaviour for `.osw` inputs

- **Summary:** the standalone runner creates a unique directory but for `.osw` inputs runs next
  to the input file regardless (`workingDirectory = dirname(oswPath)`), and this overload has no
  lock file: two parallel runs of the same OSW share one `run/` folder silently. `.osm` inputs
  are unaffected (the generated OSW lands in the unique directory).
- **Files:** `Classes/OpenStudioSimulationRunner.cs:331-368`.
- **Proposed correction (follow-up):** copy the OSW into the unique directory with
  absolutised `seed_file`/`weather_file` paths, or apply the same lock-file guard to the OSW
  directory. Requires OSW-rewrite design — intentionally not changed in this pass.
- **Status:** **Open (follow-up)** — recorded; not blocking (default GH/API path uses the
  context-based runner or `.osm` inputs).

### P2-05 — Grasshopper lifecycle: no cancellation on component removal / document close

- **Summary:** `GH_SamAsyncComponent` cancels a stale run on input change and on `cancel_`, but
  deleting the component or closing the document leaves the CLI/EnergyPlus run executing to
  completion (bounded only by the timeout); the completion continuation then calls
  `ScheduleSolution` on a document that may be disposed. Rhino *process* exit is already safe
  (kill-on-close Job Object).
- **Files:** `Grasshopper/SAM.Analytical.Grasshopper.OpenStudio/Component/GH_SamAsyncComponent.cs`.
- **Correction:** override `RemovedFromDocument` and `DocumentContextChanged` (close) to cancel
  the token; guard the completion continuation.
- **Status:** **Fixed** (Stage L) — `RemovedFromDocument` cancels the running task (process
  tree dies via the runner's kill path); `DocumentContextChanged` cancels on `Close` only
  (lock/unload/document-switch leave the run alive); the completion continuation re-resolves
  `OnPingDocument()` and never schedules into a removed/disposed document (exception-guarded).
  Full solution (incl. GH project) builds clean; headless tests cannot exercise the Grasshopper
  runtime — human confirmation is §10 step 5. Commit in §8.

### P2-06 — Encoding corruption in the coverage files

- **Summary:** `docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md` contains three double-encoded
  arrows (bytes `C3 A2 E2 80 A0 E2 80 99` — `â†’` — instead of `E2 86 92` `→`);
  `tests/resources/openstudio-analytical-coverage.json` contains one raw CP1252 em-dash byte
  (0x97) — invalid UTF-8 — in the `Result.Space.GlazingExternalConduction` note (tests survive
  because `File.ReadAllText` substitutes U+FFFD).
- **Correction:** rewrite the affected bytes as proper UTF-8; content otherwise unchanged.
- **Status:** **Confirmed — fix pending** (Stage L).

## 6. P3 observations (recorded, not fixed)

| # | Observation | Location |
| --- | --- | --- |
| P3-01 | GH components re-run the full simulation whenever an unchanged definition recomputes (F5/reopen) — standard Grasshopper expiry semantics (Ladybug parity), but a signature-keyed result cache would avoid multi-second re-runs | `GH_SamAsyncComponent.cs` |
| P3-02 | `Statistics.UnsupportedObjects` increments only for SAM-OS-IC-001; CON-002/MAT-002 unsupported parameters are counted via skips/diagnostic counts instead | `OpenStudioConversionContext.cs:122` |
| P3-03 | `SimpleGlazing.setUFactor` return value unchecked — U > 7 W/m²K (EnergyPlus limit) would silently keep the OS default | `Construction.cs:97` |
| P3-04 | `ContentHash` uses `MD5.Create()` — throws on FIPS-enforcing hosts; non-cryptographic use, SHA-256 would be safer | `InternalCondition.cs:358` |
| P3-05 | `SAM-OS-HVAC-001` XML summary said "Conditioned zone missing setpoints" while the code also carries deferred-HVAC info diagnostics (summary widened in the P1-01 commit) | `OpenStudioDiagnosticCodes.cs` |
| P3-06 | Unique GUID run directories are never cleaned up — unbounded disk growth under long-lived output directories (documented trade-off for traceability) | `OpenStudioSimulationRunner.cs` |
| P3-07 | The traceability ObjectMap registers the first OpenStudio variant per SAM Guid only (Forward construction, first schedule type); multi-variant traceability lives in the caches (documented) | `OpenStudioObjectMap` usage |
| P3-08 | Test run directories accumulate inside the test `bin` output (2 GB+ observed); per-run cleanup happens at start, not end | test fixtures |

## 7. Areas reviewed with no defect found

- **Coverage system (Stage D):** 279 entries, zero duplicates; status totals match the claim
  exactly; MD ↔ JSON in perfect lockstep (279 ↔ 279, no drift); every Unsupported entry
  declares a diagnostic; enum reflection is deterministic (`Enum.GetNames` + set membership);
  new SAM enum members cannot vanish silently (live→manifest direction enforced; the missing
  reverse direction is P2-01). Milestone totals (MVP 90, C1 3, C2 37, C3 23, C4 26, C5 36,
  C0 64) reproduce. *Stage D correction:* this review originally recorded "every id maps to a
  live SAM enum member — no stale entries today"; the P2-01 reverse check proved that wrong —
  `AnalyticalMaterialParameter.TypeName`/`.Description` named commented-out members. Corrected
  totals after Stage L: **277 entries, NA 56, C0 62**.
- **C1 adapter hardening (Stage E):** ordinal case-insensitive `IsConditioned` (tr-TR tested);
  schedule cache keyed (Guid, ProfileType); SpaceType key = name + content hash (Guids
  stripped) + per-space evaluated densities — first-space imposition impossible (tested);
  BuildingStory space-elevation fallback correct with information diagnostic; conversion errors
  gate the CLI (tested); conditioned zero-surface zones rejected with error (tested); result
  owns the model, disposal idempotent (SWIG-guarded), use-after-dispose throws (tested);
  VersionTranslator load path; every SQL query parameterised (verified in all five query
  helpers; quoted-name test passes); statistics source/created/skipped/error counts populated
  and snapshotted (tested); repeated conversion+disposal loop clean.
- **C2 internal conditions (Stage F):** `OccupancyGain` = sensible + latent (verified in SAM
  source) → People activity level total W/person with SensibleHeatFraction S/(S+L) — correct
  E+ semantics; latent equipment as dedicated instance (latent=1, radiant=0) keeps fraction
  sums at 1.0 with no double counting (sensible and latent SAM queries are disjoint — verified);
  radiant fractions with documented defaults; native ACH infiltration + per-exterior-area
  fallback; DSOA method Sum with per-person/area/ACH/absolute (honeybee parity) and the
  space-level override precedence; humidistat schedules in percent (0–100 limits) — E+ expects
  percent, and the independent run proves 40 %/60 % setpoints act at the right scale;
  heating-only/cooling-only single-mode thermostats without invented setpoints (tested);
  unconditioned zones get no thermostat/Ideal Loads; profile 8760/8784 rules, leap tiling,
  day-composition rotation, gap errors — all verified in code and tests.
- **C3 fenestration (Stage G):** frame width from `DefaultFrameWidth` else frame thickness;
  conductance U = 1/Σ(t/λ) — film-free, matching the EnergyPlus frame-conductance definition;
  absorptances 1 − External*Reflectance; pane polygon from SAM's own `GetPaneFace3Ds` with
  area-conservation guard (pane strictly inside aperture, ≥ minimum area); frameless fallback
  warnings on every invalid-frame path (missing material/thickness/conductivity, degenerate
  pane, area failure); frames skipped on opaque doors (info); full-polygon-plus-frame
  double-counting impossible (pane polygon only when a frame object exists — verified in code
  and E+ output: 2.470 + 0.330 = 2.800 m²); internal pairing reciprocal incl. framed interzone
  windows (probe: clean run); opaque/glazed door families; AirGap vs WindowMaterial:Gas
  context separation (P1-06 machinery intact); holes reported with Guid + geometry summary,
  never fabricated; blind/shade/opening-property diagnostics fire (aperture-level).
- **C4 site/weather (Stage H):** NorthAngle radians→degrees with no inversion — the identical
  transform SAM_Tas applies to `TAS3D.Building.northAngle` (authoritative production
  convention), proven end-to-end by the 180° solar shift; explicit `NorthAngleDegrees`
  precedence (tested); SAM Location precedence with information diagnostic (P2-03 adds
  validation); ground temperatures SAM → EPW header (SAM.Weather parser — OpenStudio's
  `setWeatherFile` does not import them; verified against the pinned EPW header values) → named
  18 °C default warning; RunPeriod/timestep/calendar/leap/DST/solar distribution/shadow
  frequency all applied and tested; DDY import via EnergyPlusReverseTranslator; sizing flags
  enabled with imported days; annual sums restricted to EnvironmentType 3 — proven by identical
  annual totals with 3 sizing environments present, and by the SQL cross-check.
- **C5 results (Stage I):** one unit authority (J→kWh ÷3.6e6 for annual sums; J/interval→W
  ÷3600 for peaks — roles verified); annual = SUM, peak = MAX with its own row's timestamp;
  E+ 1-based Month/Day/Hour → 0-based hour-of-year correct for non-leap (P1-03 fixes leap);
  the C5-era day-clamp fix (29–31 → DaysInMonth) verified correct for ordinary months;
  design-day rows excluded from annual and peak paths (proven); sizing results accessible via
  ZoneSizes (2 rows in run 3); zero distinct from missing (absent zones absent from
  dictionaries — tested with an unconditioned second zone); series only on request (verified);
  the three-namespace zone identity (Ideal Loads system / ThermalZone / Space enclosure keys,
  shared 8-hex Guid suffix, case-insensitive) — normalisation verified against live SQL keys in
  run 5; apostrophes/quotes sanitised out of names (MVP P2-02) and parameterised SQL besides;
  uppercase handled ordinal-ignore-case; renamed enclosure solar variable used; ToSAM writes
  the existing SAM parameters with matching units (kWh/kW/h/m²/m³ — verified against the SAM
  enum attributes).
- **C6 async/cancellation (Stage J):** `RunAsync` = `Task.Run` (no pre-await blocking);
  cancellation registered on the token kills the Job-Object tree (same path as timeout);
  asynchronous pipe reads (no ReadToEnd deadlock); progress via `IProgress` (thread-safe
  posting); unique GUID directories traceable via result paths; non-unique lock file honoured,
  released in `finally` (failure/cancel-safe — tested); partial runs cannot read as success
  (success requires exit 0 + no fatal + SQL); cancel-before/cancel-during, parallel unique
  dirs, collision, sequential, progress-order and no-surviving-process all covered by real
  CLI tests; Rhino exit kills orphans by Job-Object close.
- **C7 validation (Stage K):** fixtures assert real semantics (densities, fractions, layer
  order, areas, diagnostics, E+ artifacts); no non-null-only assertions found in the C-suites;
  simulation tests fail (never skip) when the CLI/EPW is missing; output directories deleted
  before use or GUID-unique; no inter-test order coupling found (state is per-test);
  culture-independence exercised (tr-TR test) and invariant formatting used throughout;
  determinism compares full sorted (IddObject, name) object sets — semantic, handle-free;
  performance measured on identical fixtures; disposal loop asserts validity each round;
  SimpleGlazing fallback physically justified (U/SHGC/VT are exactly the SimpleGlazingSystem
  model inputs), aperture-level precedence implemented, explicit info diagnostic + error on
  incomplete parameters (both tested).

## 8. Corrections implemented (Stage L)

*Completed after the Stage L corrections — commit SHAs and regression tests are recorded here
once each fix lands. Planned commits, one per finding:*

| Commit | Finding | Regression tests |
| --- | --- | --- |
| pending | P1-01 | declared-diagnostic tests (one per emission site) + clean-fixture silence |
| pending | P1-02 | exact heating/cooling pair from the pinned DDY; no `Hum_n`/`Wind` days; sizing rerun |
| pending | P1-03 | synthetic leap-year SQL: distinct Feb 28/29 hour indices |
| pending | P2-01 | stale-manifest-id detection |
| pending | P2-02 | non-hourly frequency warning |
| pending | P2-03 | invalid Location keeps the EPW site with a warning |
| pending | P2-05 | compile-time only (GH runtime is human-validated; §10 step 5) |
| pending | P2-06 | byte-level encoding verification |

## 9. Final validation (Stage M)

*To be completed after the corrections: final build, full suite count, post-fix driver rerun
(five scenarios + special validations) and coverage totals.*

## 10. Rhino 8 validation procedure (human)

1. Close Rhino. Build: `dotnet build SAM_OpenStudio.sln -c Debug -p:Platform=x64 -m:1`
   (the post-build step deploys to `%APPDATA%\SAM`).
2. Open Rhino 8 → Grasshopper → confirm `SAMAnalytical.ToOpenStudio` and `OpenStudio.RunModel`
   appear in the SAM ▸ OpenStudio category and load without assembly errors.
3. Feed a production SAM AnalyticalModel JSON + EPW + output directory, `_run = true`: the
   component must show "Running" without freezing the canvas, complete onto the outputs
   (osmPath/oswPath/sqlPath/heating/cooling/diagnostics/successful), and report the documented
   diagnostics only.
4. Set `cancel_ = true` mid-run: the run stops within seconds, outputs stay cleared, and no
   `openstudio.exe`/`energyplus.exe` remains in Task Manager.
5. **New (P2-05):** start a run, then (a) delete the component mid-run and (b) in a second run,
   close the document mid-run — in both cases the CLI/EnergyPlus processes must disappear
   within seconds and Rhino must stay stable. Close Rhino itself mid-run: no surviving
   EnergyPlus process.
6. With a DDY supplied (run options), verify the diagnostics name both the heating 99.6% and
   the cooling .4% day (P1-02).
7. Compare heating/cooling against the TAS production route for the same model; attach findings
   to the PR review.

## 11. Remaining limitations

Unchanged from the status document: detailed HVAC deferred (Ideal Loads remains the system);
dividers/muntins N/A (no SAM data); blinds/shades and opening properties unsupported with
diagnostics; SAM hourly design days approximated (DDY import is the deterministic path); STAT
parsing deferred; DST default off; emitter characteristics and exhaust flows deferred to the
HVAC programme (now with the declared deferral diagnostics actually emitted). New follow-ups
from this review: P2-04 (`.osw` standalone-run isolation) and the full frequency-aware peak
extraction behind P2-02.

## 12. Recommendation

*Pending Stage L/M completion — issued once every confirmed P0/P1 is resolved with regression
tests and the final validation evidence is recorded above.*
