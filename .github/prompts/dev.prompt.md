---
description: "Task-driven implementation with integrated research, Memory MCP, sequential thinking, and strict structure/reuse policy"
mode: agent
tools: ["Basic"]
---

# /dev — Task-Driven Implementation Workflow

Execute a development task sourced from Memory MCP with: context discovery → research → plan → implement → test → log → link. Use **Sequential Thinking MCP** to structure reasoning. Follow `memory-workflow.instructions.md` (**Fetch → Validate → Write → Link → Log → Summarise**) for all memory updates.

## Inputs
- Task ID: ${input:task_id:Enter the task ID to implement}
  - If no task ideas provided then assume task description ${input:description} and create a task.
- Description: ${input:description:Brief description of what you're implementing}
- Acceptance Criteria (optional): ${input:acceptance:Bulleted ACs or spec text}

---

## 1) Task Context Discovery (Memory MCP) + Relationship Validation

**Retrieve task entity**
- If Task ID is provided: call **Memory MCP** `open_nodes` with `names=["${input:task_id}"]`.
  - If reference is vague: call `search_nodes` with a concise query using the description or keywords from inputs.
  - If no results: call `read_graph`, list candidate tasks, and select interactively.
- If no Task ID is provided but a Task Description or Task Idea was provided then:
  - Create a new task entity with `create_entities` using the description as observations.
- If no task ideas provided then ask the user for task ideas.
  - If user provides task ideas then create tasks for each idea and relate them to the main task.
- If nothing is provided then guide the user to provide more information.

**Pull relationship context**
- Prefer `search_nodes --query="${input:task_id}"` (exact key tends to return the task + neighbors).
- Ensure you have:
  - ✅ Task entity (observations, status)
  - ✅ Dependencies (`depends_on`, `enables`, `blocks`)
  - ✅ Research & decisions (`has_research`, `builds_on`)
  - ✅ Patterns & implementations (`implements`, `uses`, `creates`)
  - ✅ Constraints (`constrained_by`, `requires`)
  - ✅ Integration points (related system components)

If any of the above are missing, fall back to `read_graph` and complete the neighborhood.

**Analyze relationships**
- Prerequisites & blockers
- Decisions that frame implementation
- Reusable patterns & prior implementations
- Integration points and blast radius
- Status of upstream tasks

> Add a brief observation on the task entity summarizing retrieved context (see §5).

---

## 2) Research & Validation (Mandatory Order)

When external info is needed, follow **exactly**:
1. `microsoft_docs_search`
2. `microsoft_docs_fetch`
3. `Context7`
4. `fetch` (vendor docs, issues, blogs)

- Summarize only facts you’ll apply. Prefer vendor/standards over blogs.
- Create/update a **decision** entity for this research:
  - Name: `research_${input:task_id}_<ISO8601-now>`
  - Observations: key findings, APIs validated, versions/constraints.
  - Relations:
    - `${input:task_id}` → `has_research` → `research_${input:task_id}_<ISO8601-now>`
    - `research_${input:task_id}_<ISO8601-now>` → `builds_on` → prior decisions (if any)

---

## 3) Pre-Implementation Plan (Checklist)

- Parse ACs from inputs + task observations.
- Review linked constraints & patterns.
- **Reuse > Refactor > New** policy:
  - First search for existing functions/components/modules that satisfy or can be **safely refactored** to satisfy the need.
  - Only create new code when reuse/refactor is not viable; document why.
- Produce a **3–7 step** plan tied to specific files and tests.
- Render as a markdown checklist (see §7).

---

## 4) Implementation (Small, Safe Diffs with Strict Structure)

- Read before writing (`#codebase`, `#search`, `#usages`).
- Apply targeted edits with `#changes` / `#editFiles`.
- **Directory structure discipline:**
  - **Never place new files at repo root** unless explicitly required by the build/tooling.
  - Place files in the **correct language/framework-conventional directories** (discover via existing project layout, workspace docs, or authoritative docs).
  - **Validate** created paths match the intended directory and convention before writing.
- **Reuse/refactor first:**
  - Prefer extending or refactoring existing code over duplication.
  - Mark deprecations or TODOs where a later consolidation is required.
- **Temporary/validation artifacts:**
  - If you must create temporary scripts, fixtures, or diagnostics:
    - Put them in an appropriate scratch/test/tools area consistent with repo conventions (or add to `.gitignore`).
    - **Remove or revert** them before finishing (see §8 Cleanup).
- Use repo scripts or tasks for builds/test runs (`#runTasks`, `#runInTerminal`)—**ask before risky ops**.
- New technical decisions:
  1) `create_entities` with `entityType="decision"` (rationale, alternatives, impact).
  2) `create_relations` linking it to the task (`implements`/`uses`/`constrained_by`, etc.).

---

## 5) Continuous Progress Updates (Memory MCP)

- Add progress to the task entity after each major step:

```txt
add_observations --observations=[{
  "entityName": "${input:task_id}",
  "contents": ["Progress: Step <n> completed — <short summary>"]
}]
````

* Capture reusable patterns as **pattern** entities (when appropriate):

```txt
create_entities --entities=[{
  "name": "pattern_<shortName>",
  "entityType": "pattern",
  "observations": [
    "Description: <one-liner>",
    "Example (language-agnostic or pseudo): <snippet or steps>",
    "When to use: <conditions>"
  ]
}]
```

* **Always create relations** between the task, decisions, research, and patterns you used:

```txt
create_relations --relations=[{
  "from": "${input:task_id}",
  "to": "pattern_<shortName>",
  "relationType": "uses"
}]
```

---

## 6) Testing & Debugging

* Discover tests with `#findTestFiles`; run with `#runTests`.
* If failing, inspect with `#testFailure`, fix, and re-run until passing.
* Add/extend **unit/integration** tests to cover ACs and edge paths.
* Validate integration points identified in §1.
* If you created temporary fixtures/tools for validation, keep them isolated and slated for cleanup (§8).

---

## 7) Todo Checklist (kept current and shown last)

Wrap the plan in triple backticks and update on progress:

```markdown
- [ ] Step 1: ...
- [ ] Step 2: ...
- [ ] Step 3: ...
```

* Display the updated checklist at the **end** of each message.
* After checking an item, **perform the next step**—don’t hand back control until done or blocked.

---

## 8) Final Validation, Cleanup & Memory Updates

**Validate against ACs**

* Confirm each acceptance criterion with evidence (tests, outputs, docs).

**Quality gates (language-agnostic)**

* Run repo-configured **linters/formatters** and **static analysis** via `#runTasks` or project scripts.
* Ensure no secrets/keys committed; validate env/managed identity usage where relevant.
* Confirm performance implications on hot paths if applicable.

**Strict cleanup**

* Remove or revert **all temporary/validation scripts, fixtures, and diagnostics**.
* Confirm **no stray files** live in the repo root or incorrect directories.
* Verify final file paths conform to **language/framework best practices** and project conventions.

**Mark complete**

```txt
add_observations --observations=[{
  "entityName": "${input:task_id}",
  "contents": [
    "Status: DONE",
    "Completed: <ISO8601 timestamp>",
    "All acceptance criteria met",
    "Quality gates passed",
    "Temporary artifacts cleaned",
    "Directory structure validated"
  ]
}]
```

**Link everything**

* Relate the task to the final decision summary, research, and any new patterns:

```txt
create_entities --entities=[{
  "name": "decision_${input:task_id}_final_<ISO8601-now>",
  "entityType": "decision",
  "observations": ["Summary: <what we built, why, and impacts>"]
}]
create_relations --relations=[
  { "from": "${input:task_id}", "to": "decision_${input:task_id}_final_<ISO8601-now>", "relationType": "has_decision" }
]
```

---

## Sequential Thinking (MCP) — Execution

For non-trivial tasks, **call `sequentialthinking` every reasoning hop** using camelCase fields:

* Required: `thought`, `thoughtNumber>=1`, `totalThoughts>=thoughtNumber`, `nextThoughtNeeded` (false only on the final synthesis).
* Optional: `isRevision`, `revisesThought`, `branchFromThought`, `branchId`.
* If the tool is unavailable, **stop and report** that the policy cannot be followed.

---

## Comms

* Before any tool call: one concise sentence stating what you’ll do and why.
* Keep explanations short and decision-focused. Cite which research tools you used (Docs, Context7, Fetch).
* Write code directly to the **correct** files; don’t dump large code in chat unless asked.
