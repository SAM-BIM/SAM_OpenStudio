# F-S11-1: OpenStudio result association by zone identity, not Guid suffix

Production fix for finding F-S11-1 of S11 (SAM_OpenStudio#34, Draft, review head `9ce146cd`; record
`documentation/S11-OpenStudio-ResultsRoundTrip-PR.md` on that branch). Owner authorised this bounded correction only
(2026-10-24, from the reviewed proposal `NEXT-ROOM-MATCHING.md`).

Branch `fix/s11-zone-result-identity`, base `sow/2026-Q4` (`ec204cbb`). **Draft, for independent review. Not merged.**
- **Tested content (revision 3, inventory-based collision detection):** `b8b38e6ed4a622ffacd914a22b027ad245d3af7b` (code + tests). Later commits on
  this branch are record-only.
- **Revision 2:** tested content `486688204c850e23330e3626473f4c05d7736007`, reviewed head
  `77dd095b46a1f0531fc69c79777edb9263f00b6b` (Build 40094711400 and SPDX 40094711395 succeeded). Independent review
  (parent-local `.review-s11-zone-identity-r2-20261024/REVIEW.md`): REVISE — collision detection used only the keys of
  the result variables read, so a room with no such output was invisible (`probe-inventory.ps1`: the unconditioned
  room's window solar was assigned to the conditioned room). Corrected below.
- **Revision 1:** tested content `13e79b045ee83a25e4ef7a60cc9ad6d060f57834`, reviewed head
  `a25b4f5aafe71f4b2b844b358231abeb89fd1220` (Build 40080452181 and SPDX 40080452124 succeeded). Independent review
  (parent-local `.review-s11-zone-identity-20261024/REVIEW.md`): REVISE — R1 numbered-name false match, R2 evidence
  wording. Both corrected in revision 2.

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

- **Identity.** Matching uses the stable identity of the generated name `SAM_<Type>_<SanitizedName>_<GuidFirst8>`: the
  part after a known `SAM_IdealLoads_` / `SAM_ThermalZone_` / `SAM_Space_` prefix, case-insensitive (EnergyPlus
  uppercases keys). The name must end with a valid 8-hex Guid suffix (`Query.TryGetGuidSuffix`). Unnamed objects reduce
  to the Guid prefix alone; sanitization and underscores are those of the writer (`Core.OpenStudio.Query.OpenStudioName`),
  which is unchanged.
- **Uniqueness from the native zone inventory (revision 3).** `ExtractResultSet` reads the SQL `Zones.ZoneName` column
  (new private `ReadZoneNames`, same reader pattern as `ReadZoneSizes`; EnergyPlus writes one row per zone whatever
  outputs were requested). `UniqueZoneIdentities` keeps an identity only if exactly one zone carries it, that zone name
  is unnumbered, and no key read by the extractor (Ideal Loads heating/cooling and every normalised dictionary) carries
  it numbered; report keys supplement, never replace, the inventory. A key is remapped only when its identity is in that
  set, the key itself is unnumbered and exactly one unnumbered zone key matches. No `Zones` table, an unreadable one, an
  empty one, a NULL name or an inventory that lacks the room: uniqueness is not established and keys stay unmapped.
  Revision 2 inferred uniqueness from absent optional outputs; collisions now do not depend on unmet-time or
  temperature output, nor on `extractTimeSeries`.
- **Numbered names.** OpenStudio de-duplicates a repeated name per object type by appending ` <n>`; generated names
  never contain spaces. A numbered name is evidence of a collision only, never a match: an unconditioned room created
  first owns the unnumbered ThermalZone/Space names while the conditioned room gets `... 1` for those but the unnumbered
  Ideal Loads name.
- **No overwrite.** If two report keys of one variable resolve to the same zone key, neither is remapped.
- The two identical `NormalizeKeys` overloads became one generic method that also takes the unique identities. No
  public API, schema, writer name or Guid format changed.
- Key formats: source-backed by `OpenStudioName`/`SanitizeName`, and observed in a genuine engine output inspected on
  this machine (machine-local, not committed): the C6 simulation test output
  `SAM_OpenStudio/tests/SAM.Analytical.OpenStudio.Tests/bin/x64/Release/net8.0/c6_collision/run/eplusout.eso`
  (EnergyPlus 25.2.0-cf7368216c, 2026-10-21; `eplusout.end`: "EnergyPlus Completed Successfully-- 12 Warning; 0 Severe
  Errors") reports `SAM_IDEALLOADS_SPACE_SINGLE_CCCCCCCC` for Ideal Loads variables,
  `SAM_THERMALZONE_SPACE_SINGLE_CCCCCCCC` for not-met time, gains, infiltration and T/T_op/RH, and
  `SAM_SPACE_SPACE_SINGLE_CCCCCCCC` for `Enclosure Windows Total Transmitted Solar Radiation Energy`; its
  `eplusout.sql` has `CREATE TABLE Zones (ZoneIndex INTEGER PRIMARY KEY, ZoneName TEXT, ...)` (no Spaces table).
  Revision 1 wrongly cited `.review-s11-r1-correction-20261024/extra-frequency-two/work/run/eplusout.eso` as a real
  run; it is synthetic review output and no S11 engine run exists.

How ambiguity is visible (existing conventions, no new diagnostic channel): the unmapped key stays in the result set
under its ThermalZone/Space name, and the SAM mapping (`ToSAM_SpaceSimulationResults`, exact Ideal Loads name lookup)
leaves that room's value unset (for example no `UnmetHours`) rather than another room's value.

## Tests

`tests/SAM.Analytical.OpenStudio.Tests/C5/ZoneResultIdentityTests.cs` (synthetic EnergyPlus-schema SQL with a native
`Zones` inventory, offline, through `ExtractLoads` + `ExtractResultSet` and, for the fixture case,
`ToSAM_SpaceSimulationResults`). 17 test cases:

| Test | Proves |
| --- | --- |
| `SharedGuidPrefix_FixAFixB_EachRoomKeepsItsOwnValues` | Original fixture identities (FixA `5080c000-…c0a1`, FixB `5080c000-…c0b1`) keep distinct unmet hours, gains, solar, infiltration and T/T_op/RH series; each SAM space gets its own UnmetHours |
| `SharedGuidPrefix_DistinctNames_NoUnmetOutput_StillMaps` (rev. 3, series on/off) | Distinct names sharing a prefix still map without not-met output, with and without series |
| `UniquePrefixes_AndDuplicateDisplayNames_MapAsBefore` | Unique prefixes unchanged; same display name with distinct prefixes still maps |
| `SanitizedUnderscoreAndEmptyNames_SharedPrefix_Match` | Whitespace sanitization, underscores in names and the empty name, all sharing one prefix |
| `SuffixOnlyMatch_IsNotGuessedOntoAnotherRoom` | An unconditioned zone sharing FixA's prefix no longer overwrites FixA; non-SAM keys pass through |
| `AmbiguousIdentity_LeavesKeysUnmapped_NeverOverwrites` | Both colliding rooms present: neither assigned, both values kept |
| `NumberedSource_OnlyUnnumberedTarget_StaysUnmapped` | Numbered ThermalZone/Space scalars and series stay under their own keys, even with an inventory that omits the numbered room (report-key evidence supplements) |
| `NumberedTarget_OnlyUnnumberedSource_StaysUnmapped` | Unnumbered scalar and series are not moved onto a numbered Ideal Loads key |
| `CollidingUnconditionedRoom_OnlySourceEntry_IsNotAssignedToTheConditionedRoom` | Window solar reported only for the unconditioned colliding room is not assigned |
| `HiddenCollision_NativeInventoryOnly_IsNotAssignedToTheConditionedRoom` (rev. 3, series on/off) | One conditioned + one colliding unconditioned room, collision visible only in `Zones`; no not-met output, no numbered result key: window solar (Space), people gains (ThermalZone) and the temperature series stay unmapped |
| `MissingIdentityMetadata_NothingIsRemapped` (rev. 3: no Zones table, empty table, NULL ZoneName, other zone only) | Uniqueness cannot be established: nothing is remapped, energies still read |
| `TwoReportKeysOnOneRoom_NeitherOverwritesTheOther` | Two report keys resolving to one zone: no overwrite |

Fail-before evidence:
- Revision 1: against the original Q4 runner 5 of the first 6 failed; the unique-prefix test passes on both.
- Revision 2: the three numbered-name tests failed 3/3 on revision-1 code `13e79b04`.
- Revision 3: with the new and inventory-carrying tests built against revision-2 code `48668820`: 6 failed / 11 passed —
  both `HiddenCollision_…` cases and all four `MissingIdentityMetadata_…` cases failed; the distinct-name/no-unmet
  cases passed on both, as intended. After the correction: 17 passed.
- The reviewer's `probe-inventory.ps1`, was **not rerun**: it needs PowerShell 7 (the test binaries are .NET 8) and none is installed on this machine (Windows PowerShell 5.1 cannot load them). Its scenario (Zones lists `SAM_THERMALZONE_FIXA_5080C000` and `... 1`, no numbered result key, the unconditioned room's window solar as the only solar entry) is reproduced by both `HiddenCollision_…` cases, which failed on `48668820` and pass now.

## Validation (local, this machine)

Release x64 with `APPDATA` redirected to a temp folder; no Grasshopper project built or deployed. The worktree's
`..\..\..\SAM\build` and `..\..\..\SAM_SQLite\build` hint paths were resolved by local directory junctions
(`.worktrees\SAM`, `.worktrees\SAM_SQLite`, machine-local) to the existing sibling builds (SAM `sow/2026-Q4`
`b7c2c36a`).

- Fresh rebuild: `dotnet build tests/SAM.Analytical.OpenStudio.Tests/SAM.Analytical.OpenStudio.Tests.csproj -c Release
  -p:Platform=x64 --no-incremental` (builds SAM.Core/Geometry/Analytical.OpenStudio and the tests): 0 errors; all output DLLs rewritten by this rebuild (15:29:55).
- `dotnet test … --no-build --filter "FullyQualifiedName~ZoneResultIdentityTests"`: 17 passed / 0 failed / 0 skipped.
- `dotnet test … --no-build --filter "TestCategory!=Simulation"` (all offline suites, including the C5
  `AddResultsBySqlTests` that run `ExtractResultSet` on synthetic SQL with a Zones table): 488 passed / 0 failed / 2 skipped (the two real-SQL reproduction fixtures are not committed; 6 m 58 s).
- No benchmark project calls `ExtractLoads`/`ExtractResultSet`/`NormalizeKeys`; not rebuilt. Hosted CI builds the
  solution and runs the existing S09 tests; it is not evidence that these main-project tests ran.
- No EnergyPlus, OpenStudio CLI, Tas or other engine run. S09/S10 evidence and S11 fixtures untouched.

GitHub checks: see the PR description for the final head.

## Compatibility and limits

- Genuine EnergyPlus SQL always has a `Zones` table, so models whose 8-character prefixes are unique, and colliding
  prefixes with distinct names, map as before. Same name + same prefix is left unmapped. An SQL file without a readable
  `Zones` table (not produced by EnergyPlus) now gets no remapping at all instead of suffix guessing.
- Residual, separate scope (not changed): `Modify/AddResults.cs` `LookupSpace`/panel lookups resolve design-day
  (ZoneSizes) and surface results by the 8-character Guid suffix first and take the first SAM object with that suffix.
  They can misassociate whenever prefixes collide, **even with different room or panel names** (for example FixA/FixB);
  this is not limited to equal names. The annual-family "matched no SAM space" diagnostic uses the same lookup. Also,
  `ToSAM_SpaceSimulationResults` looks values up by the exact generated Ideal Loads name, so two rooms with the same
  name AND prefix both read the unnumbered key. Both need separately scoped work.
- Out of scope (unchanged): F-S11-2 fractional unmet hours (SAM integer `UnmetHours`), OccupiedUnmetHours, writer
  names, fixtures, S11 tolerances/status, UI, CI.

## Next step

Independent review of revision 3 (another model/session, High, new session, Plan mode, read-only). After acceptance and
owner-approved merge, reconcile S11 #34's pinned `Finding_SharedGuidPrefix_…` test with this fix as a separately
reviewed step; F-S11-2 and the AddResults suffix lookups follow under their own scope.
