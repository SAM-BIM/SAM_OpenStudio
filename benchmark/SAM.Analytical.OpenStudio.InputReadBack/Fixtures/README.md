# S09 fixtures: the S08 signed-off equivalence fixtures, reused byte for byte

These are the two Phase B equivalence fixtures the owner signed off for S08 on 2026-10-21. They are copied unchanged from
SAM_Tas `SAM_Tas/SAM.Analytical.Tas.DirectT3D.Validation/ReadBack/Fixtures/` (added in `05edbdde`, merged in
SAM_Tas#106 `61eb0d37`). They were not regenerated: SAM mints new GUIDs on every factory call.

| File | sha256 | Bytes |
|---|---|---|
| `s08-fix-box.json` | `45524172b32f85df89511dd344141023de999faa9552a199e6b49062612acd08` | 723,176 |
| `s08-fix-two.json` | `20da9b586345e9fc67dc6308e6da1578b9bc41b3c694dbdab37e9c677959baa0` | 760,086 |

- `SHA256SUMS.txt` is the S08 file, byte for byte.
- `.gitattributes` marks this folder `-text`, so the CRLF bytes are kept.
- `Run-S09-ReadBack.ps1` and the tests check both hashes before using the files.

Provenance, declared conventions (centre-line panels with PhysicalThickness, box V2, two-zone V1/V1, closed single-pane
windows, no exhaust, systems or IZAM data) and the owner decisions are in the SAM_Tas README beside the originals.

Weather: the SAM#212 Budapest TMYx EPW from SAM `SAM/SAM/SAM.Tests/Resources/Weather/` (decompressed sha256
`472ffb4e2f10867600949a392ce46891fc1bd05f2fc8d8917a99bca7d26209c3`), the same file S08 used.
