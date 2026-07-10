# Squad Pre-Push Review Gate

A hard gate that prevents any `squad/*` branch from being pushed to the remote
until **Vasquez** (the PR Review Gatekeeper, model `gpt-5.6-sol`) has reviewed the
branch and recorded an approval for the exact commit being pushed.

## How it works

1. **The hook** — `pre-push` (this folder holds the reference copy; the active
   copy is installed at `.git/hooks/pre-push`). Because git worktrees share the
   common `.git/hooks`, this one hook enforces the gate across **every** worktree.
   It blocks any push of `refs/heads/squad/*` whose HEAD SHA has no approval
   marker. `master` and non-`squad/*` branches are never gated.

2. **Approvals** — stored in `<git-common-dir>/squad-approvals/<sha>` (i.e.
   `.git/squad-approvals/<sha>`). Keyed by commit SHA, so any new commit
   invalidates the approval and forces a fresh review.

3. **Recording an approval** — Vasquez runs, from the branch's worktree, after a
   clean review:
   ```pwsh
   pwsh -File .squad/gate/Approve-Branch.ps1 -Notes "Clean: build + tests green"
   ```

## Installing / reinstalling the hook

Worktrees share `.git/hooks`, so installing once covers all of them:

```pwsh
Copy-Item .squad/gate/pre-push .git/hooks/pre-push -Force
```

On a fresh clone or another machine, re-run that copy. No `core.hooksPath`
change is required.

## Owner escape hatch

For a one-off push that must bypass the gate (owner only):

```pwsh
$env:SQUAD_GATE_BYPASS = "1"; git push; Remove-Item Env:SQUAD_GATE_BYPASS
```

## The rule

No `squad/*` branch is pushed, turned into a PR, or merged to `master` until
Vasquez has reviewed the branch diff and every blocking problem is resolved.
This is enforced both by the coordinator (governance — see `routing.md` and
`decisions.md`) and mechanically by the `pre-push` hook.
