# F-S11-1: OpenStudio result association by zone identity, not Guid suffix

Production fix for finding F-S11-1 of S11 (SAM_OpenStudio#34, Draft, review head `9ce146cd`; record
`documentation/S11-OpenStudio-ResultsRoundTrip-PR.md` on that branch). Owner authorised this bounded correction only
(2026-10-24, from the reviewed proposal `NEXT-ROOM-MATCHING.md`).

Branch `fix/s11-zone-result-identity`, base `sow/2026-Q4` (`ec204cbb`). **Draft, for independent review. Not merged.**
- **Tested content (revision 2, review correction R1/R2):** `486688204c850e23330e3626473f4c05d7736007` (code + tests). Later commits on this branch
  are record-only.
- **Revision 1:** tested content `13e79b045ee83a25e4ef7a60cc9ad6d060f57834`, reviewed head
  `a25b4f5aafe71f4b2b844b358231abeb89fd1220` (Build 40080452181 and SPDX 40080452124 succeeded). Independent review
  (parent-local `.review-s11-zone-identity-20261024/REVIEW.md`): REVISE — R1 numbered-name false match, R2 evidence
  wording. Both are corrected below.

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
- **Numbered names (revision 2, R1).** OpenStudio de-duplicates a repeated name per object type by appending ` <n>`;
  generated names never contain spaces. A numbered name is used only as **evidence of a collision**, never to establish
  a match: `ExtractResultSet` collects the identities of every numbered key it read (Ideal Loads heating/cooling keys
  and all normalised dictionaries) into `collidingIdentities`, and `NormalizeKey` leaves a key under its own name when it
  is numbered, when its identity is colliding, or when its only matching zone key is numbered. Cross-variable evidence
  is needed because numbering is per type: an unconditioned room created first owns the unnumbered ThermalZone/Space
  names while the conditioned room gets `... 1` for those but the unnumbered Ideal Loads name, so a quantity reported
  only for the unconditioned room would otherwise look like a unique match. Revision 1 stripped the ending and matched,
  which moved for example `SAM_THERMALZONE_FIXA_5080C000 1 = 216` onto the sole `SAM_IDEALLOADS_FIXA_5080C000`.
- The two identical `NormalizeKeys` overloads became one generic method; it now also takes the colliding identities.
  No public API, schema, writer name or Guid format changed.
- Key formats: source-backed by `OpenStudioName`/`SanitizeName`, and observed in a genuine engine output inspected on
  this machine (machine-local, not committed): the C6 simulation test output
  `SAM_OpenStudio/tests/SAM.Analytical.OpenStudio.Tests/bin/x64/Release/net8.0/c6_collision/run/eplusout.eso`
  (EnergyPlus 25.2.0-cf7368216c, 2026-10-21; `eplusout.end`: "EnergyPlus Completed Successfully-- 12 Warning; 0 Severe
  Errors") reports `SAM_IDEALLOADS_SPACE_SINGLE_CCCCCCCC` for Ideal Loads variables,
  `SAM_THERMALZONE_SPACE_SINGLE_CCCCCCCC` for not-met time, gains, infiltration and T/T_op/RH, and
  `SAM_SPACE_SPACE_SINGLE_CCCCCCCC` for `Enclosure Windows Total Transmitted Solar Radiation Energy`.
  **Correction (R2):** revision 1 cited `.review-s11-r1-correction-20261024/extra-frequency-two/work/run/eplusout.eso`
  as a real EnergyPlus/S11 run. It is synthetic test output from a review mutation probe; no S11 engine run exists.

How ambiguity is visible (existing conventions, no new diagnostic channel): the unmapped key stays in the result set
under its ThermalZone/Space name, and the SAM mapping (`ToSAM_SpaceSimulationResults`, exact Ideal Loads name lookup)
leaves that room's value unset (for example no `UnmetHours`) rather than another room's value.

## Tests

`tests/SAM.Analytical.OpenStudio.Tests/C5/ZoneResultIdentityTests.cs` (synthetic EnergyPlus-schema SQL, offline,
through `ExtractLoads` + `ExtractResultSet` and, for the fixture case, `ToSAM_SpaceSimulationResults`):

| Test | Proves |
| --- | --- |
| `SharedGuidPrefix_FixAFixB_EachRoomKeepsItsOwnValues` | Original fixture identities (FixA `5080c000-…c0a1`, FixB `5080c000-…c0b1`) keep distinct unmet hours, gains, solar, infiltration and T/T_op/RH series; each SAM space gets its own UnmetHours |
| `UniquePrefixes_AndDuplicateDisplayNames_MapAsBefore` | Unique prefixes unchanged; same display name with distinct prefixes still maps |
| `SanitizedUnderscoreAndEmptyNames_SharedPrefix_Match` | Whitespace sanitization, underscores in names and the empty name, all sharing one prefix |
| `SuffixOnlyMatch_IsNotGuessedOntoAnotherRoom` | An unconditioned zone sharing FixA's prefix no longer overwrites FixA; non-SAM keys pass through |
| `AmbiguousIdentity_LeavesKeysUnmapped_NeverOverwrites` | Both colliding rooms present (numbered and unnumbered on both sides): neither assigned, both values kept |
| `NumberedSource_OnlyUnnumberedTarget_StaysUnmapped` (rev. 2) | Numbered ThermalZone scalar, numbered Space scalar and numbered ThermalZone series stay under their own keys |
| `NumberedTarget_OnlyUnnumberedSource_StaysUnmapped` (rev. 2) | Unnumbered ThermalZone scalar and series are not moved onto a numbered Ideal Loads key |
| `CollidingUnconditionedRoom_OnlySourceEntry_IsNotAssignedToTheConditionedRoom` (rev. 2) | Window solar reported only for the unconditioned colliding room is not assigned to the conditioned room; unmet hours and series of both rooms stay unmapped |
| `TwoReportKeysOnOneRoom_NeitherOverwritesTheOther` | Two report keys resolving to one zone: no overwrite |

Fail-before evidence:
- Revision 1: against the original Q4 runner (temporarily restored, rebuilt) 5 of the first 6 failed; the unique-prefix
  test passes on both, as intended.
- Revision 2: the three new tests were added first and run against the reviewed code `13e79b04` (rebuilt): 3 failed /
  6 passed (each failed on `Expected: False But was: True`, i.e. the numbered or colliding value was moved onto the
  Ideal Loads key). After the correction: 9 passed.

## Validation (local, this machine)

Release x64 with `APPDATA` redirected to a temp folder; no Grasshopper project built or deployed. The worktree's
`..\..\..\SAM\build` and `..\..\..\SAM_SQLite\build` hint paths were resolved by local directory junctions
(`.worktrees\SAM`, `.worktrees\SAM_SQLite`, machine-local) to the existing sibling builds (SAM `sow/2026-Q4`
`b7c2c36a`).

- Fresh rebuild: `dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release
  -p:Platform=x64 --no-incremental` (builds SAM.Core/Geometry/Analytical.OpenStudio and the tests): 0 errors; all output DLLs rewritten by this rebuild.
- `dotnet test … --no-build --filter "FullyQualifiedName~ZoneResultIdentityTests"`: 9 passed / 0 failed / 0 skipped.
- `dotnet test … --no-build --filter "TestCategory!=Simulation"` (all offline suites): 480 passed / 0 failed / 2 skipped (the two real-SQL reproduction fixtures are not committed; 6 m 33 s).
- No benchmark project calls `ExtractLoads`/`ExtractResultSet`/`NormalizeKeys`; not rebuilt.
- No EnergyPlus, OpenStudio CLI, Tas or other engine run. S09/S10 evidence and S11 fixtures untouched.

GitHub checks: see the PR description for the final head.

## Compatibility and limits

- Models whose 8-character prefixes are unique, and colliding prefixes with distinct names, map as in revision 1
  (unique prefixes exactly as on Q4 before this PR). Same name + same prefix is left unmapped for these normalised
  quantities whenever a numbered name of that identity was read, whichever side is numbered.
- Residual (normalisation): the collision evidence is the numbered names the extractor read. If a colliding room's
  numbered name appears in none of the read variables, the collision cannot be seen. Not-met time is reported for
  every zone, so this needs a run that lacks it.
- Residual, separate scope (not changed): `Modify/AddResults.cs` `LookupSpace`/panel lookups resolve design-day
  (ZoneSizes) and surface results by the 8-character Guid suffix first and take the first SAM object with that suffix.
  They can misassociate whenever prefixes collide, **even with different room or panel names** (for example FixA/FixB);
  this is not limited to equal names. The annual-family "matched no SAM space" diagnostic uses the same lookup. Also,
  `ToSAM_SpaceSimulationResults` looks values up by the exact generated Ideal Loads name, so two rooms with the same
  name AND prefix both read the unnumbered key. Both need separately scoped work (full-Guid names would change IDF names
  and S09 evidence).
- Out of scope (unchanged): F-S11-2 fractional unmet hours (SAM integer `UnmetHours`), OccupiedUnmetHours, writer
  names, fixtures, S11 tolerances/status, UI, CI.

## Next step

Independent review of revision 2 (another model/session, High, new session, Plan mode, read-only). After acceptance and
owner-approved merge, reconcile S11 #34's pinned `Finding_SharedGuidPrefix_…` test with this fix as a separately
reviewed step; F-S11-2 and the AddResults suffix lookups follow under their own scope.
