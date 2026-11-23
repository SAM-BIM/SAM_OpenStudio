# S11 OpenStudio results round-trip: s08-fix-box

Verdict: **FAIL** (exit 10): 42 PASS, 12 FAIL, 0 BLOCKED of 54 required rows; 0 informational.

> Phase B remains subject to S10's declared validation limitation (SAM|FixB|Heating|designDayPeak.hourOfDay BLOCKED); an S11 PASS is not a Phase B PASS.

Run period: RunPeriod 'SAM_RunPeriod': 1/1/2006 to 12/31/2006 (8760 hours). SQL 73e7aab0b3fe3f11649ed8bc5022ef76361ce78182ae9ddcd048619046e51d20, ESO 229ca99173ddc6e01f8dc635d61f67f799f8d78b92026f6ec00824937277c90f, in.idf 572fe49eee607aa3ec824313035f30dfe2a73c1f3ca7a4b0e7bbba5d0026b651.

F3: Total and Sensible cooling are distinguishable on this run (consumption and at least one space peak >= 1000 x tolerance).

## Checks

| Check | Scope | Status | Detail |
|---|---|---|---|
| S11-C1 | calendar | PASS | RunPeriod 'SAM_RunPeriod': 1/1/2006 to 12/31/2006: 8760 expected hourly intervals |
| S11-C1 | files and RunPeriod environment | PASS | SQL environment and one ESO RunPeriod block found; files unchanged by the read |
| S11-C9 | EnergyPlus version | PASS | SQL Simulations: EnergyPlus, Version 25.1.0-1c11a3d85f, YMD=2026.11.23 13:09; ESO: Program Version,EnergyPlus, Version 25.1.0-1c11a3d85f, YMD=2026.11.23 13:09 |
| S11-C5 | identity 5080c00000000000000000000000c001 | PASS | 'FixBox' = native space 'SAM_Space_FixBox_5080c000' (6 surfaces one to one), zone 'SAM_ThermalZone_FixBox_5080c000', Ideal Loads 'SAM_IdealLoads_FixBox_5080c000'; name carrier agrees |
| S11-C5 | report keys | PASS | every SQL and ESO key is an identified Zone or Ideal Loads |
| S11-C6 | Heating 5080c00000000000000000000000c001 | PASS | Total >= Sensible every hour; annual latent 815052477.5329063 J |
| S11-C6 | Cooling 5080c00000000000000000000000c001 | PASS | Total >= Sensible every hour; annual latent 770753694.5376599 J |
| S11-C7 | Heating sql | PASS | sql DistrictHeatingWater:Facility (Zone Timestep, 52560 rows) 7584083030.328963 J vs zones 7584083030.328973 J, \|delta\| 9.5367431640625E-06 |
| S11-C7 | Heating eso | PASS | eso DistrictHeatingWater:Facility (TimeStep, 52560 rows) 7584083030.328963 J vs zones 7584083030.328973 J, \|delta\| 9.5367431640625E-06 |
| S11-C7 | Cooling sql | PASS | sql DistrictCooling:Facility (Zone Timestep, 52560 rows) 5178651842.753358 J vs zones 5178651842.753352 J, \|delta\| 5.7220458984375E-06 |
| S11-C7 | Cooling eso | PASS | eso DistrictCooling:Facility (TimeStep, 52560 rows) 5178651842.753358 J vs zones 5178651842.753352 J, \|delta\| 5.7220458984375E-06 |
| S11-C8 | sizing | INFO | ZoneSizes rows: 0 (judged against designDaySource in compare) |

## Completeness (per file, against the RunPeriod calendar)

| File | Variable | Key | Entries | Found | Missing | Duplicated | Extra | Complete |
|---|---|---|---|---|---|---|---|---|
| sql | Zone Ideal Loads Supply Air Total Heating Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Total Heating Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Total Cooling Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Total Cooling Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Sensible Heating Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Sensible Heating Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Ideal Loads Supply Air Sensible Cooling Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Ideal Loads Supply Air Sensible Cooling Energy | SAM_IDEALLOADS_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Heating Setpoint Not Met Time | SAM_THERMALZONE_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Heating Setpoint Not Met Time | SAM_THERMALZONE_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| sql | Zone Cooling Setpoint Not Met Time | SAM_THERMALZONE_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |
| eso | Zone Cooling Setpoint Not Met Time | SAM_THERMALZONE_FIXBOX_5080C000 | 1 | 8760 | 0 | 0 | 0 | yes |

## Rows

| Row | Class | Status | Reference | Subject | Delta | Margin | Reason |
|---|---|---|---|---|---|---|---|
| reference|model|-|checks | Identity | PASS |  |  |  |  | 11 checks PASS, 1 informational |
| identity|space:5080c00000000000000000000000c001|-|native | Identity | PASS |  |  |  |  | 'FixBox' = native space 'SAM_Space_FixBox_5080c000', zone 'SAM_ThermalZone_FixBox_5080c000', Ideal Loads 'SAM_IdealLoads_FixBox_5080c000' (geometry, 6 surfaces) |
| identity|space:5080c00000000000000000000000c001|-|idealLoadsKey | Identity | PASS |  |  |  |  | production key = native key 'SAM_IdealLoads_FixBox_5080c000' |
| identity|space:5080c00000000000000000000000c001|-|zoneKey | Identity | PASS |  |  |  |  | production key = native key 'SAM_ThermalZone_FixBox_5080c000' |
| identity|model|-|b1aSpaceSet | Identity | PASS |  |  |  |  | 1 B1a spaces = the SAM spaces |
| identity|model|-|b1aProvenance | Identity | PASS |  |  |  |  | Native-OpenStudio, Success, source SAM.Analytical.OpenStudio |
| closure|model|-|samPToB1a | Identity | PASS |  |  |  |  | the captured objects re-map to byte-identical B1a model and spaces sections |
| samP|model|Heating|consumption | R2 | PASS | 2106.689730646937 | 2106.6897306469355 | -1.3642420526593924E-12 | 1.07418E+08 |  |
| samP|model|Heating|peakLoad | R1 | PASS | 3.4746476441402963 | 3.4746476441402963 | 0 | 1.49322E+11 |  |
| samP|model|Heating|peakHour | R0 | PASS | 919 | 919 | 0 | same hour |  |
| samP|model|Cooling|consumption | R2 | FAIL | 1438.51440076482 | 1224.4161522821378 | -214.09824848268227 | 1.48729E+08 | \|delta\| 214.09824848268227 > R2 tolerance 1.4395144007648202E-06 |
| samP|model|Cooling|peakLoad | R1 | FAIL | 2.518387135520196 | 2.0078615819713335 | -0.5105255535488626 | 2.02719E+11 | \|delta\| 0.5105255535488626 > R1 tolerance 2.5183871355201962E-12 |
| samP|model|Cooling|peakHour | R0 | FAIL | 5030 | 4382 | -648 | hour differs | \|delta\| 648 > R0 tolerance 0 |
| samP|model|-|floorArea | R1 | PASS | 26.880000000000003 | 26.880000000000003 | 0 |  |  |
| samP|model|-|volume | R1 | PASS | 76.608 | 76.608 | 0 |  |  |
| samP|space:5080c00000000000000000000000c001|Heating|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio |
| samP|space:5080c00000000000000000000000c001|Heating|peakLoad | R1 | PASS | 3474.647644140296 | 3474.647644140296 | 0 | 1.49322E+11 |  |
| samP|space:5080c00000000000000000000000c001|Heating|peakHour | R0 | PASS | 919 | 919 | 0 | same hour |  |
| samP|space:5080c00000000000000000000000c001|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samP|space:5080c00000000000000000000000c001|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|space:5080c00000000000000000000000c001|Heating|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio, attached |
| samI|space:5080c00000000000000000000000c001|Heating|peakLoad | R1 | PASS | 3474.647644140296 | 3474.647644140296 | 0 | 1.49322E+11 |  |
| samI|space:5080c00000000000000000000000c001|Heating|peakHour | R0 | PASS | 919 | 919 | 0 | same hour |  |
| samI|space:5080c00000000000000000000000c001|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samI|space:5080c00000000000000000000000c001|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| b1a|space:5080c00000000000000000000000c001|Heating|peakLoad | R1 | PASS | 3474.647644140296 | 3474.647644140296 | 0 | 1.49322E+11 |  |
| b1a|space:5080c00000000000000000000000c001|Heating|peakHour | R0 | PASS | 919 | 919 | 0 | same hour |  |
| b1a|space:5080c00000000000000000000000c001|Heating|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| b1a|space:5080c00000000000000000000000c001|Heating|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samP|space:5080c00000000000000000000000c001|Cooling|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio |
| samP|space:5080c00000000000000000000000c001|Cooling|peakLoad | R1 | FAIL | 2518.387135520196 | 2007.8615819713336 | -510.52555354886226 | 2.02719E+11 | \|delta\| 510.52555354886226 > R1 tolerance 2.518387135520196E-09 |
| samP|space:5080c00000000000000000000000c001|Cooling|peakHour | R0 | FAIL | 5030 | 4382 | -648 | hour differs | \|delta\| 648 > R0 tolerance 0 |
| samP|space:5080c00000000000000000000000c001|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samP|space:5080c00000000000000000000000c001|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|space:5080c00000000000000000000000c001|Cooling|result | Identity | PASS |  |  |  |  | one result, source SAM.Analytical.OpenStudio, attached |
| samI|space:5080c00000000000000000000000c001|Cooling|peakLoad | R1 | FAIL | 2518.387135520196 | 2007.8615819713336 | -510.52555354886226 | 2.02719E+11 | \|delta\| 510.52555354886226 > R1 tolerance 2.518387135520196E-09 |
| samI|space:5080c00000000000000000000000c001|Cooling|peakHour | R0 | FAIL | 5030 | 4382 | -648 | hour differs | \|delta\| 648 > R0 tolerance 0 |
| samI|space:5080c00000000000000000000000c001|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| samI|space:5080c00000000000000000000000c001|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| b1a|space:5080c00000000000000000000000c001|Cooling|peakLoad | R1 | FAIL | 2518.387135520196 | 2007.8615819713336 | -510.52555354886226 | 2.02719E+11 | \|delta\| 510.52555354886226 > R1 tolerance 2.518387135520196E-09 |
| b1a|space:5080c00000000000000000000000c001|Cooling|peakHour | R0 | FAIL | 5030 | 4382 | -648 | hour differs | \|delta\| 648 > R0 tolerance 0 |
| b1a|space:5080c00000000000000000000000c001|Cooling|unmetHours | R2 | PASS | 0 | 0 | 0 |  |  |
| b1a|space:5080c00000000000000000000000c001|Cooling|designLoad | R0 | PASS |  |  |  |  | no sizing: design load absent |
| samI|model|-|unattached | Identity | PASS |  |  |  |  | 2 space results, all attached |
| b1a|model|Heating|consumption | R2 | PASS | 2106.689730646937 | 2106.6897306469355 | -1.3642420526593924E-12 | 1.07418E+08 |  |
| b1a|model|Heating|peakLoad | R1 | PASS | 3.4746476441402963 | 3.4746476441402963 | 0 | 1.49322E+11 |  |
| b1a|model|Heating|peakHour | R0 | PASS | 919 | 919 | 0 | same hour |  |
| b1a|model|Cooling|consumption | R2 | FAIL | 1438.51440076482 | 1224.4161522821378 | -214.09824848268227 | 1.48729E+08 | \|delta\| 214.09824848268227 > R2 tolerance 1.4395144007648202E-06 |
| b1a|model|Cooling|peakLoad | R1 | FAIL | 2.518387135520196 | 2.0078615819713335 | -0.5105255535488626 | 2.02719E+11 | \|delta\| 0.5105255535488626 > R1 tolerance 2.5183871355201962E-12 |
| b1a|model|Cooling|peakHour | R0 | FAIL | 5030 | 4382 | -648 | hour differs | \|delta\| 648 > R0 tolerance 0 |
| b1a|model|-|floorArea | R1 | PASS | 26.880000000000003 | 26.880000000000003 | 0 |  |  |
| b1a|model|-|volume | R1 | PASS | 76.608 | 76.608 | 0 |  |  |
| b1a|model|-|totalBasisNote | Text | PASS |  |  |  |  | notes carry the Total-basis label |
| b1a|model|-|designDaySource | Identity | PASS |  |  |  |  | designDaySource None with 0 ZoneSizes rows |
