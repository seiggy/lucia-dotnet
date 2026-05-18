# Decision: PR cleanup branches must drop generated and binary artifacts

- **Date:** 2026-05-18
- **Owner:** Parker
- **Context:** Cleaning PR #116 for merge onto current `master`

## Decision
When rescuing older PR branches, preserve the current `master` repository shape and remove accidental artifacts introduced by the stale branch. For PR #116 this meant removing tracked `.onnx` model binaries, deleting the backup `vite.config.ts.bak`, dropping the malformed `lucia-dashboard/obj` path entry, and keeping the existing `lucia.EvalHarness.Reports.HtmlReportGenerator` implementation instead of introducing a second `HtmlReportGenerator` type in `lucia.EvalHarness.Tui`.

## Why
These files are not source-of-truth application code and they either bloat the repository, represent generated output, or create avoidable merge/build failures on top of current `master`.

## Follow-up
- Keep `*.onnx` ignored in the repo.
- Prefer preserving `master` versions of tracked project metadata and generated-output exclusions during PR rescue work.
