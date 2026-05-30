### 2026-05-30T11:46:44-04:00: User directive
**By:** Zack Way (via Copilot)
**What:** Ignore GitHub Copilot code-review comments on anything under the `.squad/` folder. These are team-state/bookkeeping files and are out of scope for code review. Where possible, configure Copilot code review to exclude `.squad/` entirely so it stops commenting on those paths.
**Why:** User request — captured for team memory. Reduces review noise; `.squad/` content (decisions, history, logs, archives) is append-only bookkeeping carried along feature branches, not reviewable product code.
