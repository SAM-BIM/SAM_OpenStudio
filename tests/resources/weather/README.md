# Pinned weather fixture

**Location:** Boston Logan Intl AP, MA, USA (WMO 725090)
**Dataset:** TMYx 2004-2018 (`USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018`)
**Files:** `.epw` (weather), `.ddy` (design days), `.stat` (climate summary)
**Integrity:** see `SHA256SUMS.txt` — tests must fail if a hash does not match.

## Provenance

Copied 2026-07-18 from the local Ladybug Tools 1.8 bundle
(`C:\Program Files\ladybug_tools\resources\weather\USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018\`),
which redistributes the TMYx dataset originally published at
<https://climate.onebuilding.org/> (WMO Region 4, USA, Massachusetts).

## Licence

TMYx files from climate.onebuilding.org are free to use with attribution:
"Climatic data provided by climate.onebuilding.org". The files are unmodified.

## Why Boston

Chosen over a mild UK climate deliberately: cold winters and warm summers guarantee
both non-zero annual heating and non-zero annual cooling loads for the conditioned
one-zone fixture, which the M6 gate requires (plan section 13). No network download
is involved; the fixture is committed and immutable.
