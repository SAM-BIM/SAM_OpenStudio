# SAM Grasshopper icon redesign — SAM_OpenStudio PR record

Branch `feature/sam-gh-icon-redesign`, based on `sow/2026-Q3` @ `6972b3e`. PR: SAM-BIM/SAM_OpenStudio#22.
Propagates the SAM icon design system from SAM-BIM/SAM#166 (head `cf4d924a`, open, not merged) to this repository.

## Current status
All **6** Grasshopper objects in this repo (6 components + 0 params) use redesigned icons: **6 / 6**.
Built and validated; ready for review. **Not merged.**

## Work completed
- `design/grasshopper-icons/`: the shared SAM-BIM icon kit. `icons.py`, `render.py`, `sam_classify.py` and `ICON_DESIGN_SYSTEM.md` are vendored **verbatim** from SAM#166 (hash-checked). `icons_ext.py` and `ICON_DESIGN_SYSTEM_EXT.md` are the frozen SAM-BIM extension v1 (identical in every SAM-BIM repo). `tools/repo_rules.py` holds this repo's explicit decisions.
- **Inventory**: `tools/inventory.py` parses C# source (every non-abstract class declaring `ComponentGuid`).
- **Manifest** (source of truth): `manifest.json` / `manifest.csv` — per object: GUID, class, source, project, object glyph, operation, modifiers, icon id, resource, glyph/badge origin.
- **Generation**: 6 canonical SVGs → 24×24 PNGs; review sheet `review/contact_sheet.png` (native 24 px on GH normal / orange-warning / dark bodies + 3×) and `review/REVIEW.md`.
- **Integration**: each project's existing mechanism; only the icon token inside each `Icon` getter changes.

| Project | Objects | Icon resources | Mechanism |
|---|---|---|---|
| `SAM.Analytical.Grasshopper.OpenStudio` | 6 | 6 | resx / Bitmap |

## Design reuse
- **Reused SAM object families (3)**: `designDay`, `model`, `result`
- **New SAM-BIM ext v1 families used (0)**: — (none)
- **Verbs**: `add`, `export`, `import`, `run`; new ext verb: `run`
- Distinct icons: **6** (6 on SAM glyphs, 0 on ext glyphs). Icon ids shared with SAM render pixel-identically to SAM's.

## Decisions and assumptions
- Grammar, palette, badge families and construction rules are unchanged (SAM#166). No text, no new colours.
- Qualifier variants (`…By<X>`) share an icon intentionally (see `review/REVIEW.md`).
- Interop direction: external → SAM = import ↓, SAM → external = export ↑.
- OpenStudio → SAM = import, SAM → OpenStudio = export; `RunModel` = model + ext verb `run`. No new glyphs needed.
- Legacy icon resources are kept (still referenced by context menus / AssemblyInfo); no GUID, name, nickname, category, subcategory, parameter or behaviour change.

## Files changed
- New: `design/grasshopper-icons/**`, `<project>/Resources/Icons/SAM_GH_*.png`, `docs/GH-IconRedesign.md`.
- Modified: 6 component/param `.cs` files (one icon token each), 1× `Resources.resx`, 1× `Resources.Designer.cs`. No csproj change.

## Validation
| Check | Result |
|---|---|
| `tools/classify.py` | 6 classified, 0 unclassified |
| `tools/build.py` identical-pixel collision check | 0 groups (6 distinct icons; 0 intentionally shared icon(s) for qualifier variants, listed in `review/REVIEW.md`) |
| Icon ids shared with SAM#166 vs SAM's `png/24` | 2 shared, 2 byte-identical |
| `tools/integrate.py` re-parse | 6/6 objects reference their `SAM_GH_*` resource; every PNG exists |
| `tools/check_source.py` vs `origin/sow/2026-Q3` | vendored files OK; icon-token swaps: 6, non-icon changes: 0; base 6, now 6 -> UNCHANGED |
| `dotnet build SAM_OpenStudio.sln -c Debug` | Build succeeded, 0 errors |
| `tools/check_assemblies.py` | every assembly embeds every required 24×24 icon → OK |
| `tests/GhIconTest` (real Rhino 8 / Grasshopper, Rhino.Testing) | 6/6 objects verified: 6 load in real Rhino 8 / Grasshopper by GUID (name/category match); icon = manifest PNG (max diff 1 level, premultiplied-alpha rounding) |
| `SAM.Analytical.OpenStudio.Tests` (x64) | 349 passed, 2 skipped, 1 failed — `Manifest_CoversEveryLiveEnumMember`: pre-existing; the test's enum-coverage manifest lags newer enum members in sibling SAM (Part O / Part F parameters). The test project references only non-GH libraries, none touched here. |
| Visual review (`review/contact_sheet.png`, 24 px on normal / warning / dark bodies) | all icons legible; no collisions |

## Unresolved issues / risks
- `Manifest_CoversEveryLiveEnumMember` fails independently of this PR (enum coverage vs newer sibling-SAM enum members); needs its own fix.
- Built against sibling repos as checked out locally (SAM on `feature/sam-gh-icon-redesign` = SAM#166); icon changes are API-neutral.

## Recommended next step
Review this PR (compare `review/contact_sheet.png`), then merge by the maintainer. After merge, add the `PROJECT_PROGRESS.md` closeout entry on `sow/2026-Q3` with the merge SHA. SAM#166 (the reference design system) remains open.
