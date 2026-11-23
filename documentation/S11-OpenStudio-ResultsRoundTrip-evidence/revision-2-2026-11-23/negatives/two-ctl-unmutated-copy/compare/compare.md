# S11 OpenStudio results round-trip: s08-fix-two

Verdict: **PASS** (exit 0): 85 PASS, 0 FAIL, 0 BLOCKED of 85 required rows; 0 informational.

> Phase B remains subject to S10's declared validation limitation (SAM|FixB|Heating|designDayPeak.hourOfDay BLOCKED); an S11 PASS is not a Phase B PASS.

Run period: RunPeriod 'SAM_RunPeriod': 1/1/2006 to 12/31/2006 (8760 hours). SQL feab1b64f59d26facc2d18c37202007c9b019bcd71e631e3966374389c5cddb5, ESO b493f69f5f347cdf8d9ad58360f1f61518a294e25cdfe4adf4c2b18886130401, in.idf a088a07af52247b74f0a3a1fe88596dd2e250b67486e488a92d882c1a44857d5.

F3: Total and Sensible cooling are distinguishable on this run (consumption and at least one space peak >= 1000 x tolerance).

## Checks

| Check | Scope | Status | Detail |
|---|---|---|---|
| S11-C1 | calendar | PASS | RunPeriod 'SAM_RunPeriod': 1/1/2006 to 12/31/2006: 8760 expected hourly intervals |
| S11-C1 | files and RunPeriod environment | PASS | SQL environment and one ESO RunPeriod block found; files unchanged by the read |
| S11-C9 | EnergyPlus version | PASS | SQL Simulations: EnergyPlus, Version 25.1.0-1c11a3d85f, YMD=2026.11.23 13:09; ESO: Program Version,EnergyPlus, Version 25.1.0-1c11a3d85f, YMD=2026.11.23 13:09 |
| S11-C5 | identity 5080c00000000000000000000000c0a1 | PASS | 'FixA' = native space 'SAM_Space_FixA_5080c000' (6 surfaces one to one), zone 'SAM_ThermalZone_FixA_5080c000', Ideal Loads 'SAM_IdealLoads_FixA_5080c000'; name carrier agrees |
| S11-C5 | identity 5080c00000000000000000000000c0b1 | PASS | 'FixB' = native space 'SAM_Space_FixB_5080c000' (6 surfaces one to one), zone 'SAM_ThermalZone_FixB_5080c000', Ideal Loads 'SAM_IdealLoads_FixB_5080c000'; name carrier agrees |
| S11-C5 | report keys | PASS | every SQL and ESO key is an identified Zone or Ideal Loads |
| S11-C6 | Heating 5080c00000000000000000000000c0a1 | PASS | Total >= Sensible every hour; annual latent 35612627.61498913 J |
| S11-C6 | Cooling 5080c00000000000000000000000c0a1 | PASS | Total >= Sensible every hour; annual latent 368339665.65867126 J |
| S11-C6 | Heating 5080c00000000000000000000000c0b1 | PASS | Total >= Sensible every hour; annual latent 234260528.74992797 J |
| S11-C6 | Cooling 5080c00000000000000000000000c0b1 | PASS | Total >= Sensible every hour; annual latent 232570139.8721895 J |
| S11-C7 | Heating sql | PASS | sql DistrictHeatingWater:Facility (Zone Timestep, 52560 rows) 5599693253.219481 J vs zones 5599693253.219529 J, \|delta\| 4.76837158203125E-05 |
| S11-C7 | Heating eso | PASS | eso DistrictHeatingWater:Facility (TimeStep, 52560 rows) 5599693253.219481 J vs zones 5599693253.219529 J, \|delta\| 4.76837158203125E-05 |
| S11-C7 | Cooling sql | PASS | sql DistrictCooling:Facility (Zone Timestep, 52560 rows) 4109351916.9103966 J vs zones 4109351916.910392 J, \|delta\| 4.76837158203125E-06 |
| S11-C7 | Cooling eso | PASS | eso DistrictCooling:Facility (TimeStep, 52560 rows) 4109351916.9103966 J vs zones 4109351916.910392 J, \|delta\| 4.76837158203125E-06 |
| S11-C8 | sizing | INFO | ZoneSizes rows: 0 (judged against designDaySource in compare) |

## Completeness (per file, against the RunPeriod calendar)

| File | Variable | Key | Entries | Found | Missing | Duplicated | Extra | Complete |
|---|---|---|---|---|---|---|---|---|
| sql | Zone Ideal Loads Supply Air Total Heating Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Total Heating Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Total Cooling Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Total Cooling Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Sensible Heating Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Sensible Heating Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Sensible Cooling Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Sensible Cooling Energy | SAM_IDEALLOADS_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Heating Setpoint Not Met Time | SAM_THERMALZONE_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Heating Setpoint Not Met Time | SAM_THERMALZONE_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Cooling Setpoint Not Met Time | SAM_THERMALZONE_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Cooling Setpoint Not Met Time | SAM_THERMALZONE_FIXA_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Total Heating Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Total Heating Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Total Cooling Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Total Cooling Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Sensible Heating Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Sensible Heating Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Sensible Cooling Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Sensible Cooling Energy | SAM_IDEALLOADS_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Heating Setpoint Not Met Time | SAM_THERMALZONE_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Heating Setpoint Not Met Time | SAM_THERMALZONE_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Cooling Setpoint Not Met Time | SAM_THERMALZONE_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Cooling Setpoint Not Met Time | SAM_THERMALZONE_FIXB_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |

## Rows

| Row | Class | Status | Reference | Subject | Delta | Margin | Reason |
|---|---|---|---|---|---|---|---|
| reference|model|-|checks | Identity | PASS |  |  |  |  | 14 checks PASS, 1 informational |
| identity|space:5080c00000000000000000000000c0a1|-|native | Identity | PASS |  |  |  |  | 'FixA' = native space 'SAM_Space_FixA_5080c000', zone 'SAM_ThermalZone_FixA_5080c000', Ideal Loads 'SAM_IdealLoads_FixA_5080c000' (geometry, 6 surfaces) |
| identity|space:5080c00000000000000000000000c0a1|-|idealLoadsKey | Identity | PASS |  |  |  |  | production key = native key 'SAM_IdealLoads_FixA_5080c000' |
| identity|space:5080c00000000000000000000000c0a1|-|zoneKey | Identity | PASS |  |  |  |  | production key = native key 'SAM_ThermalZone_FixA_5080c000' |
| identity|space:5080c00000000000000000000000c0b1|-|native | Identity | PASS |  |  |  |  | 'FixB' = native space 'SAM_Space_FixB_5080c000', zone 'SAM_ThermalZone_FixB_5080c000', Ideal Loads 'SAM_IdealLoads_FixB_5080c000' (geometry, 6 surfaces) |
| identity|space:5080c00000000000000000000000c0b1|-|idealLoadsKey | Identity | PASS |  |  |  |  | production key = native key 'SAM_IdealLoads_FixB_5080c000' |
| identity|space:5080c00000000000000000000000c0b1|-|zoneKey | Identity | PASS |  |  |  |  | production key = native key 'SAM_ThermalZone_FixB_5080c000' |
| identity|model|-|b1aSpaceSet | Identity | PASS |  |  |  |  | 2 B1a spaces = the SAM spaces |
| identity|model|-|b1aProvenance | Identity | PASS |  |  |  |  | Native-OpenStudio, Success, source SAM.Analytical.OpenStudio |
| closure|model|-|samPToB1a | Identity | PASS |  |  |  |  | the captured objects re-map to byte-identical B1a model and spaces sections |
| samP|model|Heating|consumption | R2 | PASS | 1555.470348116536 | 1555.470348116533 | -2.9558577807620168E-12 | 4.81633E+07 |  |
| samP|model|Heating|peakLoad | R1 | PASS | 2.487849842801424 | 2.487849842801424 | 0 | 7.28769E+10 |  |
| samP|model|Heating|peakHour | R0 | PASS | 775 | 775 | 0 | same hour |  |
| samP|model|Cooling|consumption | R2 | PASS | 1141.48664358622 | 1141.4866435862227 | 2.7284841053187847E-12 | 1.46102E+08 |  |
| samP|model|Cooling|peakLoad | R1 | PASS | 1.497093318807568 | 1.497093318807568 | 0 | 7.99148E+10 |  |
| samP|model|Cooling|peakHour | R0 | PASS | 5701 | 5701 | 0 | hour differs |  |
| samP|model|-|floorArea | R1 | PASS | 43.739999999999995 | 43.739999999999995 | 0 |  |  |
| samP|model|-|volume | R1 | PASS | 133.40699999999998 | 133.40699999999998 | 0 |  |  |
| samP|space:5080c00000000000000000000000c0a1|Heating|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio |
| samP|space:5080c00000000000000000000000c0a1|Heating|peakLoad | R1 | PASS | 1758.0652636023904 | 1758.0652636023904 | 0 | 6.94942E+10 |  |
| samP|space:5080c00000000000000000000000c0a1|Heating|peakHour | R0 | PASS | 751 | 751 | 0 | same hour |  |
| samP|space:5080c00000000000000000000000c0a1|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samP|space:5080c00000000000000000000000c0a1|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|space:5080c00000000000000000000000c0a1|Heating|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio, attached |
| samI|space:5080c00000000000000000000000c0a1|Heating|peakLoad | R1 | PASS | 1758.0652636023904 | 1758.0652636023904 | 0 | 6.94942E+10 |  |
| samI|space:5080c00000000000000000000000c0a1|Heating|peakHour | R0 | PASS | 751 | 751 | 0 | same hour |  |
| samI|space:5080c00000000000000000000000c0a1|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samI|space:5080c00000000000000000000000c0a1|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| b1a|space:5080c00000000000000000000000c0a1|Heating|peakLoad | R1 | PASS | 1758.0652636023904 | 1758.0652636023904 | 0 | 6.94942E+10 |  |
| b1a|space:5080c00000000000000000000000c0a1|Heating|peakHour | R0 | PASS | 751 | 751 | 0 | same hour |  |
| b1a|space:5080c00000000000000000000000c0a1|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| b1a|space:5080c00000000000000000000000c0a1|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samP|space:5080c00000000000000000000000c0a1|Cooling|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio |
| samP|space:5080c00000000000000000000000c0a1|Cooling|peakLoad | R1 | PASS | 1338.0190901481403 | 1338.0190901481403 | 0 | 8.44228E+10 |  |
| samP|space:5080c00000000000000000000000c0a1|Cooling|peakHour | R0 | PASS | 5701 | 5701 | 0 | same hour |  |
| samP|space:5080c00000000000000000000000c0a1|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samP|space:5080c00000000000000000000000c0a1|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|space:5080c00000000000000000000000c0a1|Cooling|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio, attached |
| samI|space:5080c00000000000000000000000c0a1|Cooling|peakLoad | R1 | PASS | 1338.0190901481403 | 1338.0190901481403 | 0 | 8.44228E+10 |  |
| samI|space:5080c00000000000000000000000c0a1|Cooling|peakHour | R0 | PASS | 5701 | 5701 | 0 | same hour |  |
| samI|space:5080c00000000000000000000000c0a1|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samI|space:5080c00000000000000000000000c0a1|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| b1a|space:5080c00000000000000000000000c0a1|Cooling|peakLoad | R1 | PASS | 1338.0190901481403 | 1338.0190901481403 | 0 | 8.44228E+10 |  |
| b1a|space:5080c00000000000000000000000c0a1|Cooling|peakHour | R0 | PASS | 5701 | 5701 | 0 | same hour |  |
| b1a|space:5080c00000000000000000000000c0a1|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| b1a|space:5080c00000000000000000000000c0a1|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samP|space:5080c00000000000000000000000c0b1|Heating|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio |
| samP|space:5080c00000000000000000000000c0b1|Heating|peakLoad | R1 | PASS | 852.7295212465667 | 852.7295212465667 | 0 | 8.2937E+10 |  |
| samP|space:5080c00000000000000000000000c0b1|Heating|peakHour | R0 | PASS | 918 | 918 | 0 | same hour |  |
| samP|space:5080c00000000000000000000000c0b1|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samP|space:5080c00000000000000000000000c0b1|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|space:5080c00000000000000000000000c0b1|Heating|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio, attached |
| samI|space:5080c00000000000000000000000c0b1|Heating|peakLoad | R1 | PASS | 852.7295212465667 | 852.7295212465667 | 0 | 8.2937E+10 |  |
| samI|space:5080c00000000000000000000000c0b1|Heating|peakHour | R0 | PASS | 918 | 918 | 0 | same hour |  |
| samI|space:5080c00000000000000000000000c0b1|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samI|space:5080c00000000000000000000000c0b1|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| b1a|space:5080c00000000000000000000000c0b1|Heating|peakLoad | R1 | PASS | 852.7295212465667 | 852.7295212465667 | 0 | 8.2937E+10 |  |
| b1a|space:5080c00000000000000000000000c0b1|Heating|peakHour | R0 | PASS | 918 | 918 | 0 | same hour |  |
| b1a|space:5080c00000000000000000000000c0b1|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| b1a|space:5080c00000000000000000000000c0b1|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samP|space:5080c00000000000000000000000c0b1|Cooling|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio |
| samP|space:5080c00000000000000000000000c0b1|Cooling|peakLoad | R1 | PASS | 374.9020383782116 | 374.9020383782116 | 0 | 4.24922E+11 |  |
| samP|space:5080c00000000000000000000000c0b1|Cooling|peakHour | R0 | PASS | 5033 | 5033 | 0 | hour differs |  |
| samP|space:5080c00000000000000000000000c0b1|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samP|space:5080c00000000000000000000000c0b1|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|space:5080c00000000000000000000000c0b1|Cooling|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio, attached |
| samI|space:5080c00000000000000000000000c0b1|Cooling|peakLoad | R1 | PASS | 374.9020383782116 | 374.9020383782116 | 0 | 4.24922E+11 |  |
| samI|space:5080c00000000000000000000000c0b1|Cooling|peakHour | R0 | PASS | 5033 | 5033 | 0 | hour differs |  |
| samI|space:5080c00000000000000000000000c0b1|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samI|space:5080c00000000000000000000000c0b1|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| b1a|space:5080c00000000000000000000000c0b1|Cooling|peakLoad | R1 | PASS | 374.9020383782116 | 374.9020383782116 | 0 | 4.24922E+11 |  |
| b1a|space:5080c00000000000000000000000c0b1|Cooling|peakHour | R0 | PASS | 5033 | 5033 | 0 | hour differs |  |
| b1a|space:5080c00000000000000000000000c0b1|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| b1a|space:5080c00000000000000000000000c0b1|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|model|-|unattached | Identity | PASS |  |  |  |  | 4 space results, all attached |
| b1a|model|Heating|consumption | R2 | PASS | 1555.470348116536 | 1555.470348116533 | -2.9558577807620168E-12 | 4.81633E+07 |  |
| b1a|model|Heating|peakLoad | R1 | PASS | 2.487849842801424 | 2.487849842801424 | 0 | 7.28769E+10 |  |
| b1a|model|Heating|peakHour | R0 | PASS | 775 | 775 | 0 | same hour |  |
| b1a|model|Cooling|consumption | R2 | PASS | 1141.48664358622 | 1141.4866435862227 | 2.7284841053187847E-12 | 1.46102E+08 |  |
| b1a|model|Cooling|peakLoad | R1 | PASS | 1.497093318807568 | 1.497093318807568 | 0 | 7.99148E+10 |  |
| b1a|model|Cooling|peakHour | R0 | PASS | 5701 | 5701 | 0 | hour differs |  |
| b1a|model|-|floorArea | R1 | PASS | 43.739999999999995 | 43.739999999999995 | 0 |  |  |
| b1a|model|-|volume | R1 | PASS | 133.40699999999998 | 133.40699999999998 | 0 |  |  |
| b1a|model|-|totalBasisNote | Text | PASS |  |  |  |  | notes carry the Total-basis label |
| b1a|model|-|designDaySource | Identity | PASS |  |  |  |  | designDaySource None with 0 ZoneSizes rows |
