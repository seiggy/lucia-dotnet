# Parker's History Archive — Pre-June 2026 Work

Archived condensed entries from May 2026 and earlier.

---

## 2026-05-29: Hosts & Agent-Cores Health Review Summary

Health findings from whole-solution review:
- Scalar/OpenAPI browsable pre-auth in both hosts
- InternalTokenAuthenticationHandler non-constant-time comparison
- DateTime.Now vs UtcNow mixed in MusicAgent
- HmacSessionService transient signing keys
- ScheduledTaskService removes tasks before firing
- Positive patterns: AgentProxyApi SSRF allowlist, AgentRegistrationHealthCheck diagnostics

---

## 2026-05-25: Agent Timeout Handling (Bug #106)

AgentInvokerOptions.Timeout defaults to 30 seconds. Upstream cancellations (voice timeout, HTTP disconnect) propagate into orchestration. Fixed with descriptive user-facing failures and CancellationToken.None in event recording.

---

## 2026-05-18: PR #116 Cleanup for Merge

Stripped accidental artifacts:
- Removed tracked .onnx model binaries
- Removed backup config files
- Dropped malformed path entries
- Kept correct HtmlReportGenerator version

---

## Pre-May 2026 Releases (Summary)

- **EXCLUDE_SPEECH flag** — Jetson/ARM64 builds
- **Embedding provider switches** — Cache bypass + rebuild
- **Conversation API audit** — 3 fast-path patterns
- **Config poll intervals** — 5s→30s, fixed Mongo race condition
- **SQLite NULL safety** — Aggregate result guards

---

## Architecture Notes (Stable)

- Deployment modes: Standalone vs Mesh
- Conversation fast-path: CommandPatternRouter → DirectSkillExecutor → LLM
- Two-tier prompt caching (routing + chat)
- Model provider system: OpenAI/Azure/Ollama/Anthropic/Gemini/OpenRouter
