# S09 acceptance pack, 2026-10-23 (revision 12, S07 §5.2 amendment U1)

The authorised acceptance run for SAM_OpenStudio#33 at head `d26665bd2e397d011cd1dda382c3e93004e1c231` (tested code
content `8edf793a63b274cafe5e55b9e85fa597d6307a10`), base `7f216657bd03b740644ae12d6ff099396ee997e0`. It replaces
nothing: the earlier pack (`../runs/`, `../negatives/`, `../summary-*.json`, revisions 1-4, `acc3`) is kept as history.
No engine, simulation or sizing was run: the export writes and translates with the OpenStudio SDK only (3.10.0+86d7e215a1).

## Binaries (Release x64, built from `d26665b`)

| File | SHA-256 |
|---|---|
| `os-input-readback.dll` (harness) | `8a77b2353fd288f34ce49a52845541c97d94856b96d0ad821980cf1d4046db5c` |
| `SAM.Analytical.OpenStudio.InputReadBack.dll` (reader/comparator) | `d2368ce851b869138b386697e4ef3c9e74b2cb86ef80077133172378517ed4a5` |
| `SAM.Analytical.OpenStudio.dll` (writer) | `67a2ded78a22c9b83f5b536dc7b202314a686fe8c7718119afb676714a84e798` |
| `SAM.Analytical.InputEquivalence.dll` (shared core, SAM_Tas `c50e9169`) | `0c01d8926bb07528eef38c69bf1e54bb9664849a850e8185e56b2e6dac77a7ac` |

The DLL hashes differ from the revision-12 test session (`revision-12/test-summaries.txt`) because the build stamps the
current commit into each assembly; the source is the same (`d26665b` adds documentation only to `8edf793`).

Inputs: `s08-fix-box.json` `45524172…acd08`, `s08-fix-two.json` `20da9b58…baa0` (checked by the script against
`Fixtures/SHA256SUMS.txt`); the SAM#212 Budapest EPW `472ffb4e…09c3` (checked by the script).

## Results (approved rules)

| Run | Exit | Status | Coverage | Out-of-engine-scope | `report-approved.txt` SHA-256 |
|---|---|---|---|---|---|
| `box` | 0 | PASS | 319/319 | 105 (69 differing, 36 equal) | `868a5808d4cc1a96513e54b8189e647c8f45f4375b058a940ac1dee719b959fe` |
| `two` | 0 | PASS | 592/592 | 216 (138 differing, 78 equal) | `9e3a3f0ed0e2fd6d8703154a0cb71a69ffa10344d1e4e253c29886840a6c466d` |
| `box-repeat` | 0 | PASS | 319/319 | 105 (69, 36) | `868a5808…b959fe` (identical to `box`) |

- PASS is scoped native-input conformance of the OpenStudio export to S07: not simulation equivalence, not a Phase B
  PASS, not an owner acceptance of anything else. Gated non-PASS: 0 on every run.
- Determinism: `box` and `box-repeat` give byte-identical `in.idf` (`46a6ce6f…6baa`) and `report.txt`. The OSM differs
  only by OpenStudio's random object handles (`summary-acceptance.json`, as in every pack since revision 2). Both
  `report.txt` are byte-identical to the revision-12 test session (`../revision-12/`).
- Provisional rules (baseline, `report-provisional.txt`): box exit 0; two-zone exit 12, because that pre-decision rule set
  has no owner-approved T-VENT-INACTIVE, so the two V1 zones' six inactive-ventilation rows stay G1 (UNLISTED-DEFAULT,
  proposed). The verdict is the approved rule set.

## Negatives (approved rules; each a disposable copy of `box` or `two`, read and compared fresh)

All 21 FAIL (exit 10), each by its intended mechanism, and the originals' IDFs are unchanged
(`summary-acceptance-negative.json`):

| Case | Mechanism in its report |
|---|---|
| identity-swap-names | identity mismatch: EXPORT-DEFECT 60, OUT-OF-TOLERANCE 24. The two swapped walls lose verified identity, so their 36 unused-face rows are not excluded (out-of-engine-scope 180) |
| schedule-swap | DSOA schedule consistency; 8b / 6 rows |
| v1-unscheduled | 8b EXPORT-DEFECT; inactive transformation not verified (NOT RUN) |
| missing-dsoa-link | OSM/IDF effective DSOA consistency; 8b EXPORT-DEFECT |
| missing-schedule-object | dangling schedule consistency; ventilation rows unreadable |
| people-density | 4a/4b/8a/8b OUT-OF-TOLERANCE |
| layer-conductivity | 3a/3b OUT-OF-TOLERANCE (6) |
| osm-dsoa-schedule | OSM/IDF DSOA schedule consistency |
| zone-multiplier, window-multiplier, north-axis | consistency against SAM intent and OSM/IDF |
| zone-multiplier-both, window-multiplier-both, north-axis-both | consistency against SAM intent only |
| osm-missing-thermal-zone, osm-missing-subsurfaces | missing OSM counterpart |
| lighting-previous-mapping, -visible-zero, -radiant-is-p, -return-air, -swapped | T-LIGHT-SPLIT rows |

In every negative except identity-swap-names, the out-of-engine-scope count is unchanged (105 / 216) and no
informational row hides the failure.

## Reproduce

```text
dotnet build benchmark/SAM.Analytical.OpenStudio.InputReadBack.Cli/SAM.Analytical.OpenStudio.InputReadBack.Cli.csproj -c Release -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance -Root <new dir> -WeatherEpw <budapest.epw> -Exe <full path to the CLI os-input-readback.exe>
powershell -NoProfile -ExecutionPolicy Bypass -File benchmark/SAM.Analytical.OpenStudio.InputReadBack/Run-S09-ReadBack.ps1 -Phase acceptance-negative -Root <same dir> -WeatherEpw <budapest.epw> -Exe <same exe>
powershell -NoProfile -ExecutionPolicy Bypass -Command "$PSDefaultParameterValues['Get-Content:Encoding'] = 'UTF8'; & '<full path to Pack-S09-Evidence.ps1>' -Root '<same dir>' -Target '<full path to a new evidence subfolder>'"
```

Under Windows PowerShell 5.1 (no PowerShell 7 here), two script defaults don't work as written; neither script was
changed:
- `$PSScriptRoot` is empty in the parameter defaults, so `-Exe` and `-Target` were passed explicitly.
- `Get-Content` reads the BOM-less UTF-8 reports as ANSI, which turns every "§" into "Â§". A first pack showed this
  in 27 files and was discarded, then repacked with the encoding default above. The packed `report-approved.txt` files
  (3 runs, 21 negatives) are byte-identical to the raw run.

Machine-local (not committed): the raw root `C:\TasOut\s09\acc7-u1` (OSM, IDF, `native.json`, console logs; their
hashes are in each `export.json` and `report-approved.json`). The EPW was the hash-checked copy the S09 tests decompress
from the SAM checkout.
