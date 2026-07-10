# Ceremonies

> Team meetings that happen before or after work. Each squad configures their own.

## Design Review

| Field | Value |
|-------|-------|
| **Trigger** | auto |
| **When** | before |
| **Condition** | multi-agent task involving 2+ agents modifying shared systems |
| **Facilitator** | lead |
| **Participants** | all-relevant |
| **Time budget** | focused |
| **Enabled** | ✅ yes |

**Agenda:**
1. Review the task and requirements
2. Agree on interfaces and contracts between components
3. Identify risks and edge cases
4. Assign action items

---

## Retrospective

| Field | Value |
|-------|-------|
| **Trigger** | auto |
| **When** | after |
| **Condition** | build failure, test failure, or reviewer rejection |
| **Facilitator** | lead |
| **Participants** | all-involved |
| **Time budget** | focused |
| **Enabled** | ✅ yes |

**Agenda:**
1. What happened? (facts only)
2. Root cause analysis
3. What should change?
4. Action items for next iteration

---

## Pre-Push Review Gate

| Field | Value |
|-------|-------|
| **Trigger** | auto |
| **When** | before |
| **Condition** | any `squad/*` branch is about to be pushed to the remote or turned into a PR |
| **Facilitator** | Vasquez |
| **Participants** | branch author |
| **Time budget** | focused |
| **Enabled** | ✅ yes |

**Agenda:**
1. Diff the branch against `origin/master` merge-base; confirm scope matches the issue.
2. Build + run relevant tests; separate real regressions from the known pre-existing eval-test failures.
3. Correctness pass: concurrency, resource leaks, null/auth/edge cases, security.
4. Constitution pass: one-class-per-file, tests present, nullable, telemetry retained, Conventional Commits + trailer.
5. Verdict: **APPROVE** → record approval marker for HEAD SHA (`.squad/gate/Approve-Branch.ps1`); **REQUEST-CHANGES** → numbered file:line blocking checklist, branch stays blocked, author fixes, re-review.

> Vasquez runs on **GPT-5.6 Sol only**. No `squad/*` branch is pushed, PR'd, or merged without an APPROVE. Enforced mechanically by `.git/hooks/pre-push`.
