# S11 Revision 2 attempt, 2026-10-26: BLOCKED before engine execution

> Phase B remains subject to S10's declared validation limitation (`SAM|FixB|Heating|designDayPeak.hourOfDay` BLOCKED);
> an S11 PASS is not a Phase B PASS.

**Outcome:** BLOCKED. **Engine attempts used: 0 of 2.** No simulation, readback, compare or negative ran. This folder
is bounded evidence of the stopped run-phase preflight. It is not acceptance evidence.

## Files

| File | What it is |
|---|---|
| `summary-run.json` | `Run-S11-Results.ps1 -Phase run` summary: `STOPPED`, exit 70, no runs, reason `R:\SAM is not clean at the receipt commit 457defbb…` |
| `s11-build-receipt.json` | Receipt of the successful controlled build (`Build-S11-Receipt.ps1`): four clean HEADs, 51 `.dll`/`.exe` hashes |
| `SHA256SUMS.txt` | Written by `Pack-S11-Evidence.ps1 -Root C:\TasOut\s11\r2-20261026 -Label revision-2-2026-10-26` |

## Build provenance

| Item | Value |
|---|---|
| SAM_OpenStudio | `01344e659528ec49a746d9c165ee3914d7b1cca0` |
| SAM | `457defbb033fa4a58128f2395b87587f0210c608` |
| SAM_Tas | `922044fa9ca35062979689f4e359ea85bee577dd` |
| SAM_Validation | `2a3acfafb3919b4a0b81d67ef16a4b9d1e222f5d` |
| SAM_SQLite | `ec2eb86d00cac6e4cadf52b429f9d293eeabbd5e` (built first, stamped; not part of the receipt's commit list) |
| Receipt SHA-256 | `c057cf47ca82c2eb5d61fd5ea2f714a059184d19bc48cf4a748747ceffa2f602` |
| `SAM.Core.SQLite.dll` | `3d1d9033a4edce3644c65a15644b2e96513a25f722684a03c806659825e514ee` |
| EPW | `472ffb4e2f10867600949a392ce46891fc1bd05f2fc8d8917a99bca7d26209c3` |
| Fixtures | box `45524172…acd08`, two `20da9b58…9baa0` |
| CLI | portable `3.10.0+86d7e215a1` (`--version` checked; never started for a simulation) |

**Line endings.** `SHA256SUMS.txt` hashes the files as packed on Windows, with CRLF. Git stores these JSON files with LF
(no `.gitattributes` rule covers `documentation/`) and restores CRLF on a Windows `core.autocrlf=true` checkout, where the
sums verify. LF blob hashes: `summary-run.json` `aeb17bd65aa2f64edd115b44d875968a13ab62837eb88d0a827fa0c36cdd4da9`;
`s11-build-receipt.json` `23552ab6978c69cf46aa0531d421c7f20d7d34da5e2d53c90850d95c24d8d3c8`.

The `R:` paths in the receipt are a temporary drive mapping of
`SAM-BIM\.s11r2-20261026\ws`, used for Windows path length. The mapping was removed after the session.

## Cause

- `Run-S11-Results.ps1:103-106`: the `Git` helper calls `& git`, which PowerShell resolves to the helper itself.
- The recursion overflows silently: errors are swallowed by `2>$null` and `ErrorActionPreference = 'Continue'`. After
  about 200 s the helper returns an empty HEAD, so `Test-Receipt` stops every phase with exit 70.
- The checkout was clean. The offline driver tests use receipts without commits (`S11DriverTests.cs:102`), so they could
  not catch this.

A bounded correction is recommended, not implemented, in `../../S11-OpenStudio-ResultsRoundTrip-PR.md`
("Revision 2 execution (2026-10-26): BLOCKED").

## Machine-local (not committed; copy before switching computers)

- `C:\TasOut\s11\r2-20261026\summary-run.json` (`192ec65f…`), the acceptance root.
- `SAM-BIM\.s11r2-20261026\`:
  - `scripts\` (`env.ps1`, `build.ps1`, `phase.ps1`, diagnostics);
  - `logs\` (build logs, `phase-run.log`, `phase-commands.txt`, `diag-receipt-1.txt`, `proof-git-recursion.txt`,
    `engine-check.txt`);
  - `hashes\`, `weather\budapest.epw`, and the `ws\` clones with their builds.
