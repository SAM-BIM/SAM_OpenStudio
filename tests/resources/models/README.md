# Pinned analytical-model fixtures

Real SAM models exported from Rhino/Grasshopper during human validation, committed so the
behaviour they exposed stays covered headlessly. They are read-only inputs — tests must never
write back to them.

## `rawModel-WindowsWeatherDataDDYAdiabaticAirPanel.sam`

**Provenance:** exported 2026-07-21 from
`SAM_daily\2026-07-19 OpenStudio\Openstudio-Run.ghx` (London TMYx), the same production
definition that drove the human-Rhino validation rounds.

**Contents:** 2 spaces (`Cell 1`, `Cell 2`), 11 panels, embedded `WeatherData` +
heating/cooling design days.

| Feature | In the model |
| --- | --- |
| `PanelType.Air` partition | 1 panel between the two cells, no construction (Guid `3487704c…`) |
| `PanelParameter.Adiabatic` | `true` on both `SlabOnGrade` floors (Guids `fa554084…`, `9a75a51f…`) |
| Apertures | 10 windows across the six external walls |
| Embedded weather | `WeatherData` with hourly years, plus both design days |

Covers, in one real model rather than a synthetic fixture: the adiabatic flag overriding the
`SlabOnGrade` → Ground mapping, the air boundary shared across a paired partition, and the
opt-in `AirBoundaryAirChangesPerHour` inter-zone mixing.

**Integrity:** see `SHA256SUMS.txt`.
