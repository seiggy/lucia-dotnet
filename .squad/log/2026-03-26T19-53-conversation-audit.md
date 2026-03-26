# Session Log — Conversation API Audit

**Date:** 2026-03-26  
**Time:** 19:53–20:05 UTC  
**Duration:** ~12 minutes  
**Participants:** Parker (Backend), Ash (Data Engineer), Zack Way (Director)

## Session Context

User directive to audit conversation API fast-path behavior following pattern analysis:
> "Tighten fast-path to exact-match entity resolution only. If data isn't cached, bail to orchestrator immediately. If parsed entity name isn't a 100% match from cached data, defer to LLM."

User also requesting optional personality rendering for fast-path responses (user-configurable, adds latency).

## Work Performed

### Parker — Conversation Pipeline Deep Audit (19:53–20:13)

**Mission:** Document complete request flow, reverse-engineer pattern matching, identify 5 reliable patterns, document broken behaviors.

**Key Discoveries:**

1. **Architecture:** HTTP entry point → `ConversationCommandProcessor` → pattern router → skill executor → LLM fallback (SSE streaming)
2. **Pattern engine:** Token-based matching (not regex), confidence = 0.5 + constrained match (+0.3) + zero leftover (+0.1) − penalties
3. **Confidence formula:** "turn on kitchen lights" = 0.9 (high), "turn off kitchen lights in 5 minutes" = 0.65 (caught by accident via leftover penalty)
4. **5 patterns:** light-on-off, climate-set-temp, climate-comfort, scene-activate, climate-get-status
5. **Broken behaviors:** Climate missing fan/HVAC/timer, entity resolution all string-matching, no semantic understanding
6. **Accidental bail:** Temporal commands fail via leftover token penalty, not by design — fragile

**Output:** `.squad/decisions/inbox/parker-conversation-audit.md` (372 lines, 22.7 KB)

**Impact:** Technical foundation for fast-path tightening and pattern registry migration.

### Ash — GitHub Issue Analysis (19:53–20:05)

**Mission:** Classify 12 real-world conversation failures into fast-path (6), orchestrator (3), handoff (3). Map root causes.

**Key Findings:**

1. **Fast-path failures (6):** Entity resolution on aliases (#105, #103), cache failures (#38), no color pattern, tool name mismatches, STT corrections insufficient
2. **Orchestrator failures (3):** Garbled STT over-confident (#106), entity translation breaks (#84), task rehydration lost (#58)
3. **Handoff failures (3):** Multi-turn state not persistent, Docker orchestrator unreachable, SLM fallback double-fails
4. **Success criteria:** Exact HA names, single action, English, no temporal/ambiguity
5. **Recommendations:** Keep 3 patterns, remove 8+, fix 5 architectural gaps

**Output:** `.squad/decisions/inbox/ash-conversation-issues.md` (161 lines, ~8 KB)

**Impact:** Prioritized roadmap for pattern coverage, entity resolution strategy, STT quality detection.

## Zack Way's Directives (20:05)

### Directive 1: Fast-Path Exact-Match Enforcement
- **What:** Kill fuzzy search. Require exact area/entity matches only.
- **Cache-aware:** If resolution requires cache load, bail to orchestrator instead of waiting.
- **Match requirement:** 100% string match from cached data only.
- **Rationale:** Simple commands instant, complex/ambiguous defer to LLM immediately.

### Directive 2: Optional Personality Rendering
- **What:** Add optional mode where fast-path responses routed through user's personality prompt.
- **Alternative:** Not replacing canned templates, offering choice.
- **Latency:** Acknowledged added delay.
- **User-configurable:** Feature flag or setting.

## Decision Inbox State (After Analysis)

| File | Lines | Status | Decision Type |
|------|-------|--------|--------------|
| parker-conversation-audit.md | 372 | Complete | Technical audit |
| ash-conversation-issues.md | 161 | Complete | Issue analysis |
| copilot-directive-20260326T2005.md | 9 | Complete | User directives |

**Total:** 542 lines, 3 inbox files ready for merge into `decisions.md`.

## Deduplication Notes

- **Parker & Ash overlap:** Both identify entity resolution as root cause, both find colloquial name matching failures. Non-duplicative — Parker is technical deep-dive, Ash is issue-based classification.
- **Copilot directives:** Separate from analyses, directive-based (what to do vs. what we found). Keep distinct.
- **Recommendation merging:** Parker identifies "remove X from fast-path", Ash recommends "add STT quality gate" — complementary, not duplicate.

## Next Steps

1. Merge 3 inbox files into `decisions.md` with deduplication
2. Delete inbox files
3. Git commit all `.squad/` changes
4. Implementation roadmap (fast-path tightening, climate pattern expansion, entity resolution redesign)

---

**Session End:** 2026-03-26T20:05:00Z  
**Output Quality:** ✅ High  
**Actionability:** ✅ Clear next steps documented  
**Blockers:** None identified
