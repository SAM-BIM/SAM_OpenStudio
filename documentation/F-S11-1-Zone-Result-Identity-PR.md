# F-S11-1: OpenStudio result association by zone identity, not Guid suffix

Production fix for finding F-S11-1 of S11 (SAM_OpenStudio#34, Draft, review head `9ce146cd`; record
`documentation/S11-OpenStudio-ResultsRoundTrip-PR.md` on that branch). Owner authorised this bounded correction only
(2026-10-24, from the reviewed proposal `NEXT-ROOM-MATCHING.md`).

Branch `fix/s11-zone-result-identity`, base `sow/2026-Q4` (`ec204cbb`). **Draft, for independent review. Not merged.**
- **Tested content:** `13e79b045ee83a25e4ef7a60cc9ad6d060f57834` (code + tests). Later commits on this branch are record-only.

## Defect

`OpenStudioSimulationRunner.NormalizeKey` remapped zone-level report keys (ThermalZone name) and enclosure keys (Space
name) onto the Ideal Loads energy keys by the **last 8 characters** and returned the **first** match. Rooms whose SAM
Guids share the first 8 hex digits (signed-off `s08-fix-two`: FixA and FixB, both `5080c000`) collided: one room's
not-met hours, gains, window solar, infiltration and temperature/RH series overwrote the other's, and the other room
got none.

## Change

`SAM_OpenStudio/SAM.Analytical.OpenStudio/Classes/OpenStudioSimulationRunner.cs` only (the one shared helper, used by
every `NormalizeKeys` call in `ExtractResultSet`: unmet heating/cooling hours, people/lights/equipment gains, enclosure
window solar, net infiltration, air/operative temperature and RH series).

- Matching uses the stable identity of the generated name `SAM_<Type>_<SanitizedName>_<GuidFirst8>`: the part after a
  known `SAM_IdealLoads_` / `SAM_ThermalZone_` / `SAM_Space_` prefix, case-insensitive (EnergyPlus uppercases keys).
  The name must end with a valid 8-hex Guid suffix (`Query.TryGetGuidSuffix`). Unnamed objects reduce to the Guid
  prefix alone; sanitization and underscores are those of the writer (`Core.OpenStudio.Query.OpenStudioName`), which is
  unchanged.
- A key is remapped only when exactly one zone key has its identity. No match, more than one match, no known prefix or
  no Guid suffix: the key passes through under its own name (established unmatched behaviour); there is no suffix-only
  fallback.
- If two report keys of one variable resolve to the same zone key, neither is remapped, so no dictionary entry is
  overwritten.
- OpenStudio de-duplicates a repeated name by appending ` <n>`; generated names never contain spaces, so that ending
  is ignored when computing the identity. Two rooms with the same sanitized name AND the same Guid prefix therefore
  share one identity and are left unmapped (ambiguous), instead of being paired by order.
- The two identical `NormalizeKeys` overloads became one generic method; call sites are unchanged.
- Key formats verified: real EnergyPlus 25.1 output (S11 review run, parent-local
  `.review-s11-r1-correction-20261024/extra-frequency-two/work/run/eplusout.eso`) reports `SAM_IDEALLOADS_FIXA_5080C000`
  and `SAM_THERMALZONE_FIXA_5080C000`; a 25.1 ESO in `_ext_validation/edl-ost-pilot` confirms enclosure variables key
  on the Space name (`ZONE ONE SPACE`), which for SAM is `SAM_Space_<name>_<guid8>`.

How ambiguity is visible (existing conventions, no new diagnostic channel): the unmapped key stays in the result set
under its ThermalZone/Space name, and the SAM mapping (`ToSAM_SpaceSimulationResults`, exact Ideal Loads name lookup)
leaves that room's value unset (for example no `UnmetHours`) rather than another room's value.

## Tests

New `tests/SAM.Analytical.OpenStudio.Tests/C5/ZoneResultIdentityTests.cs` (synthetic EnergyPlus-schema SQL, offline,
through `ExtractLoads` + `ExtractResultSet` and, for the fixture case, `ToSAM_SpaceSimulationResults`):

| Test | Proves |
| --- | --- |
| `SharedGuidPrefix_FixAFixB_EachRoomKeepsItsOwnValues` | Original fixture identities (FixA `5080c000-…c0a1`, FixB `5080c000-…c0b1`) keep distinct unmet hours, gains, solar, infiltration and T/T_op/RH series; each SAM space gets its own UnmetHours |
| `UniquePrefixes_AndDuplicateDisplayNames_MapAsBefore` | Unique prefixes unchanged; same display name with distinct prefixes still maps |
| `SanitizedUnderscoreAndEmptyNames_SharedPrefix_Match` | Whitespace sanitization, underscores in names and the empty name, all sharing one prefix |
| `SuffixOnlyMatch_IsNotGuessedOntoAnotherRoom` | An unconditioned zone sharing FixA's prefix no longer overwrites FixA; non-SAM keys pass through |
| `AmbiguousIdentity_LeavesKeysUnmapped_NeverOverwrites` | Same name + prefix (OpenStudio ` 1` de-duplication): neither room is assigned, both values kept |
| `TwoReportKeysOnOneRoom_NeitherOverwritesTheOther` | Two report keys resolving to one zone: no overwrite |

Against the original runner (temporarily restored, rebuilt) 5 of the 6 fail; the unique-prefix test passes on both, as
intended.

## Validation (local, this machine)

Release x64, `dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release
-p:Platform=x64` with `APPDATA` redirected to a temp folder; no Grasshopper project built or deployed. The worktree's
`..\..\..\SAM\build` and `..\..\..\SAM_SQLite\build` hint paths were resolved by local directory junctions to the
existing sibling builds (SAM `sow/2026-Q4` `b7c2c36a`).

- Build: 0 errors.
- `dotnet test … --no-build --filter "FullyQualifiedName~ZoneResultIdentityTests"`: 6 passed / 0 failed / 0 skipped.
- `dotnet test … --no-build --filter "TestCategory!=Simulation"` (all offline suites): 477 passed / 0 failed / 2 skipped (the two real-SQL reproduction fixtures are not committed; 9 m 18 s).
- No EnergyPlus, OpenStudio CLI, Tas or other engine run. S09/S10 evidence and S11 fixtures untouched.

GitHub checks: see the PR description for the final head.

## Compatibility and limits

- Models whose 8-character prefixes are unique map exactly as before. Colliding models change from silently wrong to
  correct (distinct names) or to visibly unmapped (same name + same prefix).
- Residual, not fixed here: rooms with identical sanitized name AND Guid prefix still produce identical generated
  names, so the exact-name SAM mapping and the design-day/surface suffix lookups in `Modify/AddResults.cs`
  (`LookupSpace`, Guid-suffix first) cannot tell them apart. Removing that needs full-Guid OpenStudio names, which change
  IDF names and S09 evidence: separate owner decision.
- Out of scope (unchanged): F-S11-2 fractional unmet hours (SAM integer `UnmetHours`), OccupiedUnmetHours, writer
  names, fixtures, S11 tolerances/status, UI, CI.

## Next step

Independent review of this PR (another model/session, High, read-only). After acceptance and merge, reconcile S11
#34's pinned `Finding_SharedGuidPrefix_…` test with this fix as a separately reviewed step; F-S11-2 follows under its
own compatibility review.
