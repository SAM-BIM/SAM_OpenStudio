# S11 Revision 2, 2026-11-23: real-run acceptance evidence (local)

> Phase B remains subject to S10's declared validation limitation (`SAM|FixB|Heating|designDayPeak.hourOfDay` BLOCKED);
> an S11 PASS is not a Phase B PASS.

**Outcome: PASS on both signed-off fixtures** (local acceptance; hosted CI runs only the offline tests).

- **Engine attempts used: 2 of 2** (box and two-zone, one attempt each, no retry). Both `SIMULATED`, exit 0;
  EnergyPlus `25.1.0-1c11a3d85f` "Completed Successfully", 0 severe errors, 10 warnings each.
- **readback: PASS** (exit 0). box 54/54 PASS; two-zone 85/85 PASS; 0 FAIL, 0 BLOCKED, 0 global blocks. Closure
  (captured SAM-P objects re-map to byte-identical B1a `model` and `spaces`) PASS on both.
- **Completeness:** every (zone x required quantity) present once in each file with exactly the 8760 hours of the
  RunPeriod calendar (`SAM_RunPeriod` 1/1-12/31/2006): box 6 SQL + 6 ESO series, two-zone 12 + 12; 0 missing,
  duplicated or extra hours. One SQL environment, one ESO RunPeriod block. C5-C7 and C9 PASS; C8 no ZoneSizes (no
  design days, OD-4 scope).
- **OD-7 (Total versus Sensible): met on both fixtures.** Cooling-consumption margin 1.487e8 (box) / 1.461e8 (two) x
  tolerance; best space cooling-peak margin 2.027e11 / 4.249e11 x tolerance (requirement >= 1000).
- **negative: every case as required** (exit 0). Per fixture: unmutated control 0 (PASS); same hour 7/7 h13 deleted in
  SQL and ESO 11 (BLOCKED, S11-C1); same hour duplicated in both 11 (BLOCKED); reference Sensible cooling substituted
  into SAM-P, SAM-I and B1a 10 (FAIL: 12 rows box, 15 rows two-zone; 0 BLOCKED). Originals unchanged after every case.

## Files

| Path | What it is |
|---|---|
| `summary-run.json`, `summary-readback.json`, `summary-negative.json` | `Run-S11-Results.ps1` phase summaries |
| `box/`, `two/` | `run-manifest.json`, `b1a.json`, `sam.json`, `reference.json`, `subject.json`, `compare/compare.json`, `compare/compare.md` |
| `*.console.txt`, `*.readback.txt` | producer and harness console output |
| `negatives/<label>-<case>/` | each case's `negative.json`, `reference.json`, `subject.json`, `b1a.json`, `sam.json`, `compare/` and console log |
| `s11-build-receipt.json` | the fresh controlled receipt the runs used (`72593067a631321ac97d76c142a551efa43cb93cba218eb0305ab44ea61f2ee1` as packed) |
| `SHA256SUMS.txt` | written by `Pack-S11-Evidence.ps1 -Root C:\TasOut\s11\r2-20261123 -Label revision-2-2026-11-23 -BuildReceipt <receipt>`; covers every file above |
| `offline-validation/` | added after packing (not in `SHA256SUMS.txt`): build, test, phase command logs, the offline test summary and the receipt fail-before controls; hashes below |

`offline-validation/` SHA-256 (as copied, CRLF):

- `build-commands.txt` `10696b5c1831ad9e4723befc5a45cc0dc6bfe08e6e1654f536638633565982ba`
- `test-commands.txt` `9febe86988e3b44c051a130e9032feea6f9fe52a56873dd1513a0f76c9923599`
- `test-summary.txt` `07c091ac4f222fb92fe95e98c39ee5c0f0f27e199b01bec08bb10151f3a84c4f`
- `fail-before-receipt.txt` `3c7378574476f9ea53b4ce918fc46730c6d2f41c7a70787269aac82ce44abce9`
- `phase-commands.txt` `379d1fe46d4f62333416d5ccda61241d34b804e2346b6237b3b0877fa03cd90c`

**Line endings.** `SHA256SUMS.txt` hashes the files as packed on Windows. Git stores them with LF (no
`.gitattributes` rule covers `documentation/`) and restores CRLF on a `core.autocrlf=true` Windows checkout, where the
sums verify. **Paths.** `S:` in the receipt and logs is a temporary `subst` of `SAM-BIM\.s11r3-20261123\ws` (MAX_PATH);
`C:\TasOut\...` and `C:\Users\...` paths are machine-local.

## Provenance

| Item | Value |
|---|---|
| SAM_OpenStudio (runner, tested content) | `bb74882161af1a477e6941e0d58be07e836dd53f` |
| SAM | `f8f8eb6465de4508c37af2d2c94662deee3fb240` (remote `sow/2026-Q4` 2026-11-23) |
| SAM_Validation | `2a3acfafb3919b4a0b81d67ef16a4b9d1e222f5d` |
| SAM_Tas | `922044fa9ca35062979689f4e359ea85bee577dd` |
| SAM_SQLite | `ec2eb86d00cac6e4cadf52b429f9d293eeabbd5e` (built first, stamped; not in the receipt's commit list) |
| Fixtures | box `45524172b32f85df89511dd344141023de999faa9552a199e6b49062612acd08`, two `20da9b586345e9fc67dc6308e6da1578b9bc41b3c694dbdab37e9c677959baa0` |
| EPW | SAM `f8f8eb64` `HUN_BU_Budapest.Met.Center.128400_TMYx.epw.gz` (`24b14f83d7fe47c2bfe752a611150c088353e61a4dacf46354bbb57dc9e8cbc6`) decompressed: `472ffb4e2f10867600949a392ce46891fc1bd05f2fc8d8917a99bca7d26209c3` |
| CLI | portable OpenStudio `3.10.0+86d7e215a1` (`--version` verified by the driver; prepended to the child PATH) |

| File | box | two |
|---|---|---|
| `in.idf` | `572fe49eee607aa3ec824313035f30dfe2a73c1f3ca7a4b0e7bbba5d0026b651` | `a088a07af52247b74f0a3a1fe88596dd2e250b67486e488a92d882c1a44857d5` |
| `eplusout.sql` | `73e7aab0b3fe3f11649ed8bc5022ef76361ce78182ae9ddcd048619046e51d20` | `feab1b64f59d26facc2d18c37202007c9b019bcd71e631e3966374389c5cddb5` |
| `eplusout.eso` | `229ca99173ddc6e01f8dc635d61f67f799f8d78b92026f6ec00824937277c90f` | `b493f69f5f347cdf8d9ad58360f1f61518a294e25cdfe4adf4c2b18886130401` |
| `b1a.json` | `7d986d54d998905365f3498dc3763110d5f2072e6d9a36ea57d51a9f8c520ded` | `7319160a864ce3311481df3a4a42e53a44d1b767cb0b3e8627c8e5cbf574b0c1` |
| `sam.json` | `2a25640ca0c1d951dc2dddc121a4b7de0e6650912d913575839c30c7efa1dffc` | `7864ac4498c8a48a436f0839f0dc2e21466ccceb916c702869190a40277d7832` |

Full hashes are in each `run-manifest.json`. SQL, ESO, `in.idf` and the EnergyPlus work folders stay machine-local
under `C:\TasOut\s11\r2-20261123\<box|two>\work\` (hashes unchanged by every read).

## Machine-local (not committed; copy before switching computers)

- `C:\TasOut\s11\r2-20261123\` (acceptance root, incl. SQL/ESO and negatives' mutated copies).
- `SAM-BIM\.s11r3-20261123\`: `scripts\` (`env.ps1`, `build-offline.ps1`, `test-offline.ps1`, `phase.ps1`), `logs\`
  (build/test logs, TRX under `logs\trx\`, phase logs, `clone.txt`), `hashes\inputs.txt`, `weather\budapest.epw`,
  `s09-replay\`, `control\` (fail-before scripts), and the `ws\` clones with their builds.
