# Project Progress

## Branch
`sow/2026-Q3`

## Last updated
2026-09-22 - app.config cleanup closeout

## Current status
Part of the repo-family .NET Framework `app.config` cleanup: base [SAM#126](https://github.com/SAM-BIM/SAM/pull/126) plus 17 sibling PRs, all merged into `sow/2026-Q3` on 2026-09-22 (SAM first), with their branches deleted.

## Completed
- Closeout (branch `build/netfx-cleanup-closeout`): deleted `Grasshopper/SAM.Analytical.Grasshopper.OpenStudio/app.config`. The project is a net8.0-windows Library; the file held only a `System.Runtime.CompilerServices.Unsafe` binding redirect and was still emitting `build/SAM.Analytical.Grasshopper.OpenStudio.dll.config`. The first round of sibling PRs missed it.

## Decisions / assumptions
- Every project here targets `netstandard2.0` or `net8.0(-windows)` and is an `OutputType Library`. Library `.dll.config` files are never read at runtime (only the host `Rhino.exe`/`Revit.exe` config is), so the net472-era binding redirects, `<supportedRuntime>` and `loadFromRemoteSources` were inert. They only emitted stale `.dll.config` files into `build/` and `%APPDATA%\SAM`.
- No `ConfigurationManager`/`AppSettings` use in the repo; deleted files held binding/runtime config only.

## Files changed
- `Grasshopper/SAM.Analytical.Grasshopper.OpenStudio/app.config` (deleted)

## Validation
- Full `BuildAlls_v4.bat` clean rebuild with all closeout branches checked out - exit 0, 0 errors, no `.dll.config` emitted.

## Issues / blockers
- None known for this cleanup.

## Next step
- None for this cleanup. Continue with the next planned task on `sow/2026-Q3`.
