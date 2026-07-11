# Squad Pre-Push Review Gate

A hard gate that prevents any `squad/*` branch from being pushed to the remote
until **Vasquez** (the PR Review Gatekeeper, model `gpt-5.6-sol`) has reviewed the
branch and recorded an approval for the exact commit being pushed.

## How it works

1. **The hook** — the gate lives in the version-controlled `.githooks/pre-push`,
   which is the repo's active pre-push hook (see *Installing* below). It runs the
   review gate first and then the stock **Git LFS** `pre-push` step, so LFS keeps
   working. It blocks any push whose **destination** ref is `refs/heads/squad/*`
   and whose commit SHA has no approval marker. `master` and non-`squad/*`
   branches are never gated.

   > The gate classifies by the *destination* (remote) ref, so
   > `git push origin HEAD:refs/heads/squad/foo` is gated even though the local
   > ref isn't under `squad/*`.

   > **Coverage & limits.** Because `core.hooksPath` is a *relative* path, each
   > worktree runs *its own* checked-out `.githooks/pre-push` — the hook is not
   > a single copy shared across worktrees. The gate is therefore active in any
   > worktree that (a) has a checkout containing this hook (tracked executable,
   > mode `100755`, so it runs as checked out) and (b) is in a clone whose
   > `core.hooksPath` the installer has set (a shared, per-clone setting). Every future `squad/*` worktree branches from `master`
   > (which carries the hook once this change lands), so all such worktrees are
   > covered automatically. It is
   > **not** retroactively injected into a pre-existing worktree sitting on a
   > stale branch without the hook — that worktree must merge/rebase (or
   > otherwise check out) this commit to be gated. Re-running the installer alone
   > is **not** enough: it only sets `core.hooksPath` and marks the hook
   > executable; it does not check out the hook file into a stale branch. The
   > *approval markers*, by contrast,
   > live in the shared `<git-common-dir>/squad-approvals/` and are common to all
   > worktrees. The primary enforcement remains governance: the coordinator
   > routes every `squad/*` branch to Vasquez before push/PR; the hook is
   > mechanical backup.

2. **Approvals** — stored in `<git-common-dir>/squad-approvals/<sha>` (in the
   main worktree, `.git/squad-approvals/<sha>`; linked worktrees share that same
   common dir). Keyed by commit SHA, so any new commit
   invalidates the approval and forces a fresh review.

3. **Recording an approval** — Vasquez runs, from the branch's worktree, after a
   clean review:
   ```pwsh
   pwsh -File .squad/gate/Approve-Branch.ps1 -Notes "Clean: build + tests green"
   ```

## Installing / reinstalling the hook

The gate is part of `.githooks/pre-push`, and the repo activates its hooks via
`core.hooksPath=.githooks`. Run the standard installer once per clone — it sets
`core.hooksPath`, a per-clone config that all of that clone's worktrees share.
The hook is tracked executable (mode `100755`), so it runs as soon as a checkout
contains it (the installer also re-applies the executable bit as a safety net).
A worktree is therefore gated once its checkout contains this hook (every
`squad/*` worktree created from `master` after this change lands will):

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
This is enforced by the coordinator (governance — see `routing.md` and
`decisions.md`); the `.githooks/pre-push` hook is the mechanical backstop that
blocks the push itself — the prerequisite for any PR or merge.
