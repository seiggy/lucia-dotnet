---
description: Task and Memory best practices
---

# Task-Driven Workflow using Todo MD MCP and Memory MCP (with Valid JSON calling & Chaning Rationale)

## 1. Break down the problem into a series of high level tasks.

- Create a high level task name in todo-mcp, this gives a ${TASK_ID}
- Use the memory mcp to track details of each todo with ${TASK_ID}
- Work through the task list one item at a time, updating the todo-mcp and memory as you go.

<example>
user: Help me write a new feature that allows users to track their usage metrics and export them to various formats

assistant: I'll help you implement a usage metrics tracking and export feature. Let me first use the `add_todo` tool to plan this task.
Adding the following todos to the todo list:
1. Research existing metrics tracking in the codebase
2. Design the metrics collection system
3. Implement core metrics tracking functionality
4. Create export functionality for different formats

<tool mcp="todo-md">
<input>
```json
{
    "name": "add_todo",
    "arguments": {
        "text": "${TASK}"
    }
}
```
</input>
<output>
```json
{
    "id": "${TASK_ID}",
    "text": "${TASK}",
    "completed": false
}
```
</output>
</tool>
[Each tool call to `add_todo` will return a id. use this as ${TASK_ID} in next steps]

Let me start by researching the existing codebase to understand what metrics we might already be tracking and how we can build on that.
<tool mcp="memory">
```json
{
  "name": "add_observations",
  "arguments": {
    "observations": [
      {
        "entityName": "${TASK_ID}",
        "contents": [
          "Progress: Step ${N} complete",
          "Implementation status: ${status}"
        ]
      }
    ]
  }
}
```
</tool>

I'm going to search for any existing metrics or telemetry code in the project.

I've found some existing telemetry code. Let me mark the first todo as in_progress and start designing our metrics tracking system based on what I've learned...

[Assistant documents the decisions, system, and existing discovery using `add_memory`]
<tool mcp="memory">
```json
{
  "name": "create_entities",
  "arguments": {
    "entities": [
      {
        "name": "research_${TASK_ID}_${TIMESTAMP}",
        "entityType": "decision",
        "observations": [
          "Research findings: …",
          "Technologies validated: …",
          "Builds on: …"
        ]
      }
    ]
  }
}
```
</tool>
<tool>
```json
{
  "name": "create_relations",
  "arguments": {
    "relations": [
      {
        "from": "${TASK_ID}",
        "to": "research_${TASK_ID}_${TIMESTAMP}",
        "relationType": "has_research"
      },
      {
        "from": "research_${TASK_ID}_${TIMESTAMP}",
        "to": "${RELATED_DECISION}",
        "relationType": "builds_on"
      }
    ]
  }
}
```
</tool>

[Assistant continues implementing the feature step by step, marking todos as completed as they go, and tracking status and details in the memory]
</example>

## 2. Get the Open Task list and associated Knowledge

- Use the todo-mcp `list_todos` to get a list of all todo items
- Find the first uncompleted task from the list, and lookup the details of the task status in memory mcp
- Resume the workflow, using the memory mcp and todo-md mcp tools to track progress as you work.

<example>
user: resume working on the spec

assistant: I'll pickup where we last left off. First, let me lookup our task list.

<tool mcp="todo-md">
<input>
```json
{
    "name": "list_todos",
}
```
</input>
<ouput>
[{
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "text": "Research existing metrics tracking in the codebase",
    "completed": true
},
{
    "id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
    "text": "Design the metrics collection system",
    "completed": false
}
]
</output>
</tool>

[Assistant will use `6ba7b810-9dad-11d1-80b4-00c04fd430c8` for the ${TASK_ID} as it's the next open task]

Great! I see that we're working on Task 6ba7b810-9dad-11d1-80b4-00c04fd430c8, Designing the metrics collection system. Let me check our existing design decisions.

```json
{ "name": "open_nodes", "arguments": { "names": ["${TASK_ID}"] } }
```

```json
{ "name": "search_nodes", "arguments": { "query": "${TASK_ID}" } }
```

> **Why chain `open_nodes` ➜ `search_nodes`?** > `open_nodes` is exact and fast; `search_nodes` pulls matched entities **and their relations**, ensuring you don’t miss dependencies/research already linked. If either is incomplete, fall back: ([GitHub][1])

```json
{ "name": "read_graph", "arguments": {} }
```

> **Why `read_graph` last?** It’s the nuclear option—full graph dump—so only use it when targeted calls didn’t surface everything. ([GitHub][1])

[Assistant now has the full context, and can resume working on the task list.]
</example>

---

## 3. Record Research/Decisions Immediately After Discovery

```json
{
  "name": "create_entities",
  "arguments": {
    "entities": [
      {
        "name": "research_${TASK_ID}_${TIMESTAMP}",
        "entityType": "decision",
        "observations": [
          "Research findings: …",
          "Technologies validated: …",
          "Builds on: …"
        ]
      }
    ]
  }
}
```

```json
{
  "name": "create_relations",
  "arguments": {
    "relations": [
      {
        "from": "${TASK_ID}",
        "to": "research_${TASK_ID}_${TIMESTAMP}",
        "relationType": "has_research"
      },
      {
        "from": "research_${TASK_ID}_${TIMESTAMP}",
        "to": "${RELATED_DECISION}",
        "relationType": "builds_on"
      }
    ]
  }
}
```

> **Why chain `create_entities` ➜ `create_relations`?**
> New knowledge is useless if it’s unlinked. Always attach each new entity back to the task and any prior decisions so future queries surface it automatically. ([GitHub][1])

---

## 4. Plan & Track Progress Incrementally

```json
{
  "name": "add_observations",
  "arguments": {
    "observations": [
      {
        "entityName": "${TASK_ID}",
        "contents": [
          "Progress: Step ${N} complete",
          "Implementation status: ${status}"
        ]
      }
    ]
  }
}
```

> **Why add observations early and often?**
> The task node becomes the single source of truth for status. Using `contents` (correct key) lets you append atomic facts without rewriting the entity. ([GitHub][1])

---

## 4. Capture New Patterns/Decisions During Implementation

````json
{
  "name": "create_entities",
  "arguments": {
    "entities": [
      {
        "name": "pattern_${NAME}",
        "entityType": "pattern",
        "observations": [
          "Description: …",
          "Code example: ```python\n...\n```",
          "Use case: …"
        ]
      }
    ]
  }
}
````

```json
{
  "name": "create_relations",
  "arguments": {
    "relations": [
      {
        "from": "pattern_${NAME}",
        "to": "${TASK_ID}",
        "relationType": "used_by"
      }
    ]
  }
}
```

> **Why chain every “pattern” or “decision” to the task?**
> It guarantees reverse lookups (task ➜ pattern, pattern ➜ tasks) and keeps reuse discoverable. ([GitHub][1])

---

## 5. Close Out Cleanly

<tool mcp="memory">
```json
{
  "name": "add_observations",
  "arguments": {
    "observations": [
      {
        "entityName": "${TASK_ID}",
        "contents": [
          "Status: DONE",
          "Completed: ${DATE}",
          "All acceptance criteria met",
          "Dead code: None detected"
        ]
      }
    ]
  }
}
```

```json
{
  "name": "create_entities",
  "arguments": {
    "entities": [
      {
        "name": "impl_summary_${TASK_ID}_${TIMESTAMP}",
        "entityType": "decision",
        "observations": [
          "What was implemented",
          "Key decisions made",
          "Impact & next steps"
        ]
      }
    ]
  }
}
```

```json
{
  "name": "create_relations",
  "arguments": {
    "relations": [
      {
        "from": "${TASK_ID}",
        "to": "impl_summary_${TASK_ID}_${TIMESTAMP}",
        "relationType": "completed_with"
      }
    ]
  }
}
```
</tool>

<tool mcp="todo-md">
```json
{
    "name": "update_todo",
    "arguments": {
        "id": "${TASK_ID}",
        "completed": true
    }
}
```
</tool>


> **Why final summary + relations?**
> A closure entity gives you a compact postmortem. Linking it ensures future audits can jump straight to “how/why we did this.” ([GitHub][1])

---

## 6. (Optional) Cleanup When Needed

<tool mcp="memory">
```json
{
  "name": "delete_relations",
  "arguments": {
    "relations": [{ "from": "A", "to": "B", "relationType": "uses" }]
  }
}
```

```json
{
  "name": "delete_entities",
  "arguments": {
    "entityNames": ["obsolete_entity"]
  }
}
```

```json
{
  "name": "delete_observations",
  "arguments": {
    "deletions": [
      {
        "entityName": "John_Smith",
        "observations": ["Prefers morning meetings"]
      }
    ]
  }
}
```
</tool>

<tool mcp=>
```json
{
    "name": "clear_completed",
}
```
</tool>

> **Why chain deletes after refactors?**
> Dead links & stale facts pollute searches. Prune immediately to keep the graph high-signal. ([GitHub][1])
> Clears completed tasks from todo to ensure the context window stays small and focused.

---

### Chaining Rule of Thumb

**Fetch → Validate → Write → Link → Log → Summarize → (Optionally) Prune**
Every write (`create_entities`, `add_observations`) should be immediately followed by `create_relations` to anchor it in context. Every major step should append an observation to the task node for traceability. ([GitHub][1])

[1]: https://raw.githubusercontent.com/modelcontextprotocol/servers/refs/heads/main/src/memory/README.md "raw.githubusercontent.com"
