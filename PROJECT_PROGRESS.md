# Project Progress - SAM_OpenStudio (2026-Q4)

## Branch

`sow/2026-Q4` - bootstrapped 2026-10-06 from `master` `5522dd80`. Frozen Q3 record: `sow/2026-Q3` @ `68a428f4` (not modified).

## Last updated

2026-10-06 (Q4 icon-redesign migration).

## Current status

Q4 branch cut from `master` `5522dd80`, which is the exact commit pinned in SAM_Deploy's frozen Q3 baseline (`v20261006.1`). Bootstrap added only internal docs (this file, `AGENTS.md`). No product source changed. No Q4 product work has started.

## Q4 priorities

Not yet set by the owner. Record them here at the first Q4 planning pass. Known carry-over work is listed below.

## Known carry-over work

- **SAM Grasshopper icon redesign - PR #22** (`feature/sam-gh-icon-redesign` @ `a99a4338`, open, base `sow/2026-Q3`, not merged). Analysed 2026-10-06: the branch carries only its own 4 icon-only commits (`3a5bf51`, `421f988`, `ff591db`, `a99a433`) on top of Q3 commit `6972b3e7`. Those commits are not reachable from `sow/2026-Q4` (Q4 is built on the promoted `master` line), so a plain retarget would list 88 commits. Replaying exactly those commits onto `sow/2026-Q4` @ `f9b03939` is conflict-free (verified commit-by-commit with `git merge-tree`; identical to the net-diff merge). Planned action: rebase-onto Q4 as a new branch + PR, then close this one; owner-approved controlled task, not yet executed. **Update:** migrated; replacement Q4 PR SAM_OpenStudio#24 (see the icon-redesign migration section); this old PR stays open for now.

## Repository-specific next steps

- Await Q4 planning. Open PRs for Q4 work against `sow/2026-Q4`.
- Follow the continuity convention in `AGENTS.md` for every PR and closeout.

## Decisions / assumptions

- Q4 base is `master` `5522dd80`; the internal files were recovered from `sow/2026-Q3` into this branch only, never onto `master`.
- Q4 history intentionally does not contain the Q3 branch history (the maintained `master` is the promoted Q3 line, which is not a descendant of `sow/2026-Q3`); the frozen `sow/2026-Q3` branch is the permanent record.
- Historical Q2/Q3 content below is kept as evidence; its branch names, SHAs and next steps describe Q3 and are not current instructions.

## Validation

- Bootstrap verified 2026-10-06: `sow/2026-Q4` was created at exactly `5522dd80` and the push was a normal (non-forced) branch creation.

## Issues / blockers

- None at bootstrap.

## Next step

- Owner to set Q4 priorities; then start the first Q4 task from this branch.

## Q4 operational cleanup (2026-10-06)

- Reviewed every active Q2/Q3 reference in this repository on `sow/2026-Q4` (workflow branch filters, dependency-branch resolution, `.gitmodules`/validation, docs). Historical Q2/Q3 mentions (feature documentation records, the frozen Q3 section below) are intentionally unchanged.
- Changed (`f9b0393`): removed the dead `$candidates += 'sow/2026-Q2'` fallback from the dependency-branch resolution in `.github/workflows/build.yml`. No dependency repository has a `sow/2026-Q2` branch, so the entry never matched and resolution already fell through to the default branch; behaviour is unchanged (PR head ref, current sow ref, then the dependency's default branch) and no per-quarter edit is needed.
- Checked, no action: the `github.repository_owner == 'SAM-BIM'` build guard (intentional; its comment names HoareLea only to explain why the guard exists), CODEOWNERS (SAM-BIM owners), and workflow secrets (no HoareLea-named secret). The local `upstream` (HoareLea) remote is preserved.
- Carry-over: **SAM Grasshopper icon redesign - PR #22** (`feature/sam-gh-icon-redesign` @ `a99a4338`, open, base `sow/2026-Q3`, not merged). Analysed 2026-10-06: the branch carries only its own 4 icon-only commits (`3a5bf51`, `421f988`, `ff591db`, `a99a433`) on top of Q3 commit `6972b3e7`. Those commits are not reachable from `sow/2026-Q4` (Q4 is built on the promoted `master` line), so a plain retarget would list 88 commits. Replaying exactly those commits onto `sow/2026-Q4` @ `f9b03939` is conflict-free (verified commit-by-commit with `git merge-tree`; identical to the net-diff merge). Planned action: rebase-onto Q4 as a new branch + PR, then close this one; owner-approved controlled task, not yet executed.
- Full cross-repository record, migration table and owner decisions: `SAM_Deploy:sow/2026-Q4` `PROJECT_PROGRESS.md`.

## Q4 icon-redesign migration (2026-10-06)

- Old PR: SAM-BIM/SAM_OpenStudio#22 (`feature/sam-gh-icon-redesign` @ `a99a4338`, base `sow/2026-Q3`) - **preserved, open, untouched**.
- New branch `feature/sam-gh-icon-redesign-q4` cut from `sow/2026-Q4` @ `cba0b9a4`; new PR **SAM-BIM/SAM_OpenStudio#24** (base `sow/2026-Q4`), feature head `c8b5d86d`. **Not merged.**
- Replayed (old -> new, `cherry-pick -x`; commit set taken from the GitHub PR metadata): `3a5bf51`->`9300aae`, `421f988`->`1d1f967`, `ff591db`->`a212860`, `a99a433`->`6283df5`; replay-only tip `6283df5d`; plus one new docs commit `c8b5d86` pointing the PR record at the new PR. No Q3 history imported.
- Verified at the replay-only tip, before the record commit: result tree identical to the net-diff merge of the old feature onto Q4 (`e46085cfe9`); same aggregate and per-commit `git patch-id`, file set (51 files), numstat and blobs as the old PR; no workflow/`.gitmodules`/`AGENTS.md`/`PROJECT_PROGRESS.md`/solution changes. The final PR head is not tree-identical to the old feature by design (extra documentation-only commit).
- Validation: `check_source.py origin/sow/2026-Q4` OK, `check_assemblies.py` OK, local build 0 errors, relevant tests green (see the PR body); PR CI `build` success, `spdx` success; mergeable: mergeable.
- Next: owner decides whether/when to close the old PR; merge remains the maintainer's call.

---

# Historical record - 2026-Q3 (frozen)

Source: last revision of the file on `sow/2026-Q3`, commit `109441a` (the file was removed from the Q3 tip by `930bccd`; `sow/2026-Q3` tip is `68a428f4`). Preserved verbatim except that heading levels are shifted down one. Everything below describes Q3 and is not a current instruction.

## Project Progress

### Branch
`sow/2026-Q3`

### Last updated
2026-09-22 - app.config cleanup closeout

### Current status
Part of the repo-family .NET Framework `app.config` cleanup: base [SAM#126](https://github.com/SAM-BIM/SAM/pull/126) plus 17 sibling PRs, all merged into `sow/2026-Q3` on 2026-09-22 (SAM first), with their branches deleted.

### Completed
- Closeout (branch `build/netfx-cleanup-closeout`): deleted `Grasshopper/SAM.Analytical.Grasshopper.OpenStudio/app.config`. The project is a net8.0-windows Library; the file held only a `System.Runtime.CompilerServices.Unsafe` binding redirect and was still emitting `build/SAM.Analytical.Grasshopper.OpenStudio.dll.config`. The first round of sibling PRs missed it.

### Decisions / assumptions
- Every project here targets `netstandard2.0` or `net8.0(-windows)` and is an `OutputType Library`. Library `.dll.config` files are never read at runtime (only the host `Rhino.exe`/`Revit.exe` config is), so the net472-era binding redirects, `<supportedRuntime>` and `loadFromRemoteSources` were inert. They only emitted stale `.dll.config` files into `build/` and `%APPDATA%\SAM`.
- No `ConfigurationManager`/`AppSettings` use in the repo; deleted files held binding/runtime config only.

### Files changed
- `Grasshopper/SAM.Analytical.Grasshopper.OpenStudio/app.config` (deleted)

### Validation
- Full `BuildAlls_v4.bat` clean rebuild with all closeout branches checked out - exit 0, 0 errors, no `.dll.config` emitted.

### Issues / blockers
- None known for this cleanup.

### Next step
- None for this cleanup. Continue with the next planned task on `sow/2026-Q3`.
