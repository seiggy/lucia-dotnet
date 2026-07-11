# Squad Pre-Push Review Gate

A hard gate that prevents any `squad/*` branch from being pushed to the remote
until **Vasquez** (the PR Review Gatekeeper, model `gpt-5.6-sol`) has reviewed the
branch and recorded an approval for the exact commit being pushed.

## How it works

1. **The hook** — the gate lives in the version-controlled `.githooks/pre-push`,
   which is the repo's active pre-push hook (see *Installing* below). It runs the
   review gate first and then the stock **Git LFS** `pre-push` step, so LFS keeps
   working. Because git worktrees share the common git dir, this one hook
   enforces the gate across **every** worktree. It blocks any push whose
   **destination** ref is `refs/heads/squad/*` and whose commit SHA has no
   approval marker. `master` and non-`squad/*` branches are never gated.

   > The gate classifies by the *destination* (remote) ref, so
   > `git push origin HEAD:refs/heads/squad/foo` is gated even though the local
   > ref isn't under `squad/*`.

2. **Approvals** — stored in `<git-common-dir>/squad-approvals/<sha>` (i.e.
   `.git/squad-approvals/<sha>`). Keyed by commit SHA, so any new commit
   invalidates the approval and forces a fresh review.

3. **Recording an approval** — Vasquez runs, from the branch's worktree, after a
   clean review:
   ```pwsh
   pwsh -File .squad/gate/Approve-Branch.ps1 -Notes "Clean: build + tests green"
   ```

## Installing / reinstalling the hook

The gate is part of `.githooks/pre-push`, and the repo activates its hooks via
`core.hooksPath=.githooks`. Run the standard installer once per clone — it sets
`core.hooksPath` and makes the hooks executable, covering all worktrees:

```sh
./scripts/install-git-hooks.sh
```

Because the hook is version-controlled, a fresh clone only needs that one command
(no manual copying into `.git/hooks`). New commits to `.githooks/pre-push` take
effect automatically once pulled.

## Owner escape hatch

For a one-off push that must bypass the gate (owner only):

```pwsh
$env:SQUAD_GATE_BYPASS = "1"; git push; Remove-Item Env:SQUAD_GATE_BYPASS
```

## The rule

No `squad/*` branch is pushed, turned into a PR, or merged to `master` until
Vasquez has reviewed the branch diff and every blocking problem is resolved.
This is enforced both by the coordinator (governance — see `routing.md` and
`decisions.md`) and mechanically by the `.githooks/pre-push` hook.
