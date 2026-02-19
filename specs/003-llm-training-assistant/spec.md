# Feature Specification: LLM Fine-Tuning Data Pipeline

**Feature Branch**: `003-llm-training-assistant`  
**Created**: 2026-02-19  
**Status**: Draft  
**Input**: User description: "Build a fine-tuning dataset pipeline for LLM training — capture conversation traces from the orchestrator, store in a document database, provide a review dashboard for labeling positive/negative interactions, and export labeled datasets in JSONL format."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Automatic Conversation Trace Capture (Priority: P1)

As a developer, every request that flows through the Lucia orchestrator is automatically captured with full fidelity so that I have raw material for building training datasets without any manual instrumentation per-agent.

**Why this priority**: Without trace capture, there is no data to review or export. This is the foundational data pipeline that every other story depends on.

**Independent Test**: Send a natural-language request through the orchestrator (e.g., "Turn on the kitchen lights") and verify that a complete trace document is persisted containing the user prompt, routing decision, agent tool calls, tool results, and final response.

**Acceptance Scenarios**:

1. **Given** the orchestrator is running with trace capture enabled, **When** a user sends a request, **Then** a trace document is persisted within 5 seconds containing the full conversation input, all intermediate agent messages, tool call names and arguments, tool results, and the final aggregated response.
2. **Given** a request involves multiple agents (e.g., router dispatches to light agent AND music agent), **When** the request completes, **Then** a single trace document captures each agent's full conversation thread independently.
3. **Given** an agent call fails mid-execution, **When** the error is returned, **Then** the trace document still captures all messages up to and including the error, with the trace marked as errored.
4. **Given** trace capture is disabled via configuration, **When** a request is processed, **Then** no trace document is written and orchestrator performance is unaffected.

---

### User Story 2 — Review Dashboard for Labeling Interactions (Priority: P2)

As a developer or domain expert, I can browse all captured conversation traces in a web dashboard, inspect the full request/response flow, and label each interaction as positive (good training example) or negative (bad example that needs correction) so I can curate a high-quality training dataset.

**Why this priority**: Raw captured data is useless for fine-tuning until a human reviews and labels it. The dashboard is the primary tool for converting raw traces into curated training data.

**Independent Test**: Open the review dashboard, see a list of recent traces, click into one to view the full conversation flow, and successfully apply a "positive" or "negative" label with optional notes.

**Acceptance Scenarios**:

1. **Given** traces have been captured, **When** a reviewer opens the dashboard, **Then** they see a paginated list of traces showing timestamp, user prompt preview, agent(s) involved, and current label status (unlabeled/positive/negative).
2. **Given** a reviewer clicks on a trace, **When** the detail view loads, **Then** they see the complete conversation flow: user input, routing decision, each agent's tool calls with arguments and results, and the final response — all in a readable timeline format.
3. **Given** a reviewer is viewing a trace, **When** they click "Positive" or "Negative", **Then** the label is saved and the trace list updates to reflect the new status.
4. **Given** a reviewer labels a trace as negative, **When** they optionally add correction notes (what the ideal response should have been), **Then** the notes are saved alongside the label for use in training data.
5. **Given** hundreds of traces exist, **When** a reviewer uses filters (by date range, agent, label status, or search term), **Then** the list narrows to matching traces.

---

### User Story 3 — JSONL Dataset Export (Priority: P3)

As a developer, I can export all positively-labeled traces as a JSONL fine-tuning dataset where each line contains the full conversation history as "Input" and the complete agent output (tool calls, tool results, and final response) as "Output", so I can feed this directly into a fine-tuning pipeline.

**Why this priority**: Export is the end goal of the pipeline — producing a dataset file that can be used for model fine-tuning. It depends on both capture (P1) and labeling (P2) being functional.

**Independent Test**: Label at least 2 traces as positive and 1 as negative, export with "positive only" filter, and verify the resulting JSONL file contains exactly 2 lines with correctly structured Input/Output pairs.

**Acceptance Scenarios**:

1. **Given** labeled traces exist, **When** a developer triggers an export with a label filter (e.g., "positive only"), **Then** a JSONL file is generated where each line is a valid JSON object with an "input" field containing the full conversation history and an "output" field containing the complete agent response chain.
2. **Given** a trace involved tool calls, **When** it is exported, **Then** the "output" field includes each tool call (name, arguments), each tool result, and the final text response in structured format.
3. **Given** a negative-labeled trace has correction notes, **When** exported with "include corrections" option, **Then** the "output" field uses the corrected response instead of the original agent output.
4. **Given** an export is triggered, **When** the JSONL file is produced, **Then** each line is independently parseable as valid JSON (no trailing commas, proper escaping).

---

### User Story 4 — Trace Data Retention and Cleanup (Priority: P4)

As a system administrator, traces older than a configurable retention period are automatically purged to prevent unbounded storage growth, unless they have been labeled (labeled traces are retained indefinitely or until manually deleted).

**Why this priority**: Important for production operations but not needed for initial functionality.

**Independent Test**: Configure retention to 1 day, create traces older than 1 day (unlabeled), run cleanup, and verify only unlabeled old traces are removed.

**Acceptance Scenarios**:

1. **Given** a retention period of N days is configured, **When** the cleanup process runs, **Then** unlabeled traces older than N days are permanently deleted.
2. **Given** a trace older than the retention period has a positive or negative label, **When** cleanup runs, **Then** the labeled trace is preserved.

---

### Edge Cases

- What happens when the document store is temporarily unavailable? Traces should be queued or dropped gracefully without blocking the orchestrator request pipeline.
- What happens when a conversation has extremely long context (e.g., 50+ messages)? The trace should be captured in full without truncation, with the document size limited only by the store's document size limit.
- What happens when two requests arrive simultaneously for the same session? Each request produces its own independent trace document.
- What happens when an export is triggered while new traces are being written? The export should operate on a consistent snapshot — traces written after the export starts are excluded.
- How are sensitive data handled in traces (e.g., API keys in tool call arguments)? A configurable redaction mechanism should strip known sensitive fields before persistence.

## Requirements *(mandatory)*

### Functional Requirements

#### Trace Capture

- **FR-001**: System MUST capture the complete conversation lifecycle for every orchestrator request, including: user input text, session/task identifiers, routing decision (agent selection, confidence, reasoning), each selected agent's full message exchange (system prompt, user messages, assistant messages, tool calls, tool results), and the final aggregated response.
- **FR-002**: System MUST capture trace data asynchronously so that trace persistence does not add measurable latency to the user-facing request pipeline.
- **FR-003**: System MUST allow trace capture to be enabled or disabled via application configuration without requiring code changes or redeployment.
- **FR-004**: System MUST include timing information for each step in the trace: total request duration, routing duration, per-agent execution duration.
- **FR-005**: System MUST record the model identifier (deployment name) used by each agent and the router for each traced request.

#### Storage

- **FR-006**: System MUST persist trace documents in a document database that supports flexible schema and efficient querying by timestamp, agent identifier, and label status.
- **FR-007**: System MUST store each trace as a self-contained document that can be independently retrieved, updated (for labeling), and deleted.
- **FR-008**: System MUST support configurable data retention with automatic cleanup of unlabeled traces older than the retention period.
- **FR-009**: System MUST preserve labeled traces (positive or negative) regardless of retention policy until explicitly deleted.

#### Review Dashboard

- **FR-010**: System MUST provide a web-based review interface accessible from the application's management surface.
- **FR-011**: System MUST display traces in a paginated, sortable list with columns for: timestamp, user prompt (truncated preview), agent(s) involved, model(s) used, duration, and label status.
- **FR-012**: System MUST provide a detail view for each trace showing the complete conversation flow in a human-readable timeline format.
- **FR-013**: System MUST allow reviewers to apply labels ("positive", "negative", or "unlabeled") to any trace.
- **FR-014**: System MUST allow reviewers to attach correction notes to negatively-labeled traces describing the ideal response.
- **FR-015**: System MUST provide filtering by: date range, agent name, model name, label status, and free-text search over user prompts.

#### Dataset Export

- **FR-016**: System MUST export labeled traces as a JSONL file where each line is a self-contained JSON object.
- **FR-017**: Each exported JSONL line MUST contain an "input" field with the full conversation history (system prompt + user messages) and an "output" field with the complete agent response chain (tool calls, tool results, final text response).
- **FR-018**: System MUST support export filters: by label (positive only, negative only, all labeled), by date range, by agent, and by model.
- **FR-019**: System MUST support exporting corrected outputs for negative-labeled traces when correction notes are present.
- **FR-020**: System MUST produce valid JSONL where every line is independently parseable as JSON.

#### Observability

- **FR-021**: System MUST emit telemetry for trace capture operations: traces captured count, capture latency, storage errors.
- **FR-022**: System MUST emit telemetry for export operations: export duration, record count, file size.

### Key Entities

- **ConversationTrace**: The primary document representing a single orchestrator request lifecycle. Contains: trace ID, timestamp, session ID, task ID, user input, routing decision, list of agent executions, final response, total duration, capture metadata.
- **AgentExecution**: A nested record within a trace representing one agent's complete interaction. Contains: agent ID, model deployment name, message history (system prompt, user messages, assistant messages, tool calls with arguments, tool results), execution duration, success/failure status, error details if applicable.
- **TraceLabel**: The human-applied quality label for a trace. Contains: label value (positive/negative/unlabeled), reviewer notes, correction text (for negative labels), timestamp of labeling.
- **DatasetExport**: A record of a completed export operation. Contains: export ID, timestamp, filter criteria used, record count, file location/download reference.

## Assumptions

- The existing `IOrchestratorObserver` interface and `ChatHistoryCapture` pattern provide sufficient hook points to capture all needed conversation data without modifying the core orchestrator pipeline.
- MongoDB is the selected document store — it satisfies the NoSQL requirement, has excellent .NET driver support, deploys easily in Kubernetes, and keeps data local (privacy-first). Aspire.Hosting.MongoDB provides seamless local development integration.
- The review dashboard (`lucia-dashboard`) will be implemented as a React + TypeScript + Tailwind CSS single-page application created with Vite CLI, backed by .NET Minimal APIs for the backend API layer. In production, the SPA is hosted via .NET SPA hosting middleware alongside the `lucia.AgentHost` application. In development, the Vite dev server is integrated with Aspire using `CommunityToolkit.Aspire.Hosting.NodeJS.Extensions` (`AddViteApp`).
- Sensitive data redaction is limited to known patterns (e.g., API keys, tokens) via configurable regex rules — full PII detection is out of scope for initial implementation.
- The JSONL export format follows the OpenAI fine-tuning format conventions (messages array with role/content pairs) to maximize compatibility with fine-tuning pipelines.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of orchestrator requests are captured as trace documents when trace capture is enabled, with zero data loss under normal operating conditions.
- **SC-002**: Trace capture adds less than 50ms of latency to the end-to-end request pipeline (async persistence target).
- **SC-003**: A reviewer can find, inspect, and label a specific trace within 30 seconds using the dashboard.
- **SC-004**: Exported JSONL files are 100% valid — every line independently parses as valid JSON with the expected input/output schema.
- **SC-005**: The system supports at least 10,000 stored traces with sub-second dashboard load times for list views.
- **SC-006**: A complete fine-tuning dataset (capture → label → export) can be produced from 100 interactions within 1 hour of reviewer time.

