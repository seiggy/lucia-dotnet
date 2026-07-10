# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** React 19, TypeScript, Vite 7, Tailwind CSS 4, TanStack Query, React Router 7, Lucide React, XYFlow
- **Created:** 2026-03-26

## Dashboard Architecture

- **Location:** `lucia-dashboard/`
- **Entry:** `src/App.tsx` — auth gating, sidebar nav, route definitions
- **API Layer:** `src/api.ts` — REST client for AgentHost APIs
- **Styling:** Tailwind CSS 4 via `@import "tailwindcss"` and `@tailwindcss/vite`, theme tokens in CSS `@theme`
- **State:** TanStack Query for server state, React local state for UI

## Key Pages

Activity, Agents, AgentDefinitions, Alarms, CommandTrace, Configuration, Conversation, EntityLocation, Export, Lists, Login, MatcherDebug, McpServers, ModelProviders, Presence, Plugins, PromptCache, ResponseTemplates, Setup, SkillOptimizer, Tasks, TraceDetail, TraceList, VoicePlatform

## Key Components

MeshGraph, PluginRepoDialog, PluginConfigTab, RestartBanner, SkillConfigEditor, SpanTimeline, TaskTracker, ToastContainer, ToggleSwitch, EntityMultiSelect, CustomSelect, ConfirmDialog, CommandTimeline, AutoAssignPreviewModal, InputHighlight, EntityOnboardingBanner

## Learnings

<!-- Append new learnings below. -->

- **PersonalityPrompt config section**: The backend exposes personality settings via the generic config API at `GET/PUT /api/config/sections/PersonalityPrompt`. Properties: `UsePersonalityResponses` (boolean), `Instructions` (textarea), `ModelConnectionName` (model-select), `SupportVoiceTags` (boolean). Schema is defined in `ConfigurationApi.GetAllSchemas()`.
- **ConfigurationPage is generic**: The Configuration page renders all schema sections with a sidebar + form pattern. For purpose-built UIs, create dedicated components that call the same `fetchConfigSection`/`updateConfigSection` API functions.
- **Model providers for dropdowns**: Use `fetchModelProviders('Chat')` to populate model selection dropdowns. Filter to `enabled` providers. The ConfigurationPage's `ModelSelectField` pattern uses `CustomSelect` with a default "orchestrator default" option.
- **Tailwind theme tokens**: The project uses custom tokens like `bg-basalt`, `bg-charcoal`, `bg-void`, `text-light`, `text-dust`, `text-amber`, `border-stone`, `bg-amber-glow`. Use `input-focus` class for focus ring styling on inputs.
- **Strategy encoding refactor (api.ts)**: Replaced inline ternary `strategy === 'none' ? 0 : 1` in `previewAutoAssign` and `applyAutoAssign` with a centralized `STRATEGY_ENCODING` constant map and `encodeStrategy()` helper. Used `type AutoAssignStrategy = 'none' | 'smart'` and `Record<AutoAssignStrategy, number>` for type safety. Adding new strategies now requires a single change to the const map.
- **Agent editor MCP behavior**: `AgentDefinitionsPage` cannot rely on `/api/mcp-servers/status` alone because the status map reflects runtime connections only. To populate the MCP tool picker reliably, the editor now attempts to connect enabled servers on load, then discovers tools; failures render an explicit “MCP tools unavailable …” message instead of the misleading “Server not connected”.
- **SSE hook timer leak (2026-05-29 review)**: `useActivityStream.ts` schedules a reconnect `setTimeout` in `onerror` (line 58) but never clears it on unmount — its cleanup only closes the EventSource. The sibling `useCommandTraceStream.ts` does this correctly via a `retryTimerRef` cleared in the effect cleanup. When fixing reconnect logic, always store the retry timer in a ref and clear it on unmount to avoid post-unmount EventSource creation + state updates.
- **No global error boundary**: The dashboard has no React ErrorBoundary anywhere (`main.tsx` / `App.tsx`), despite repo TS guidelines requiring one. A render throw in any page blanks the whole app. Good candidate for a small high-value fix.
- **API-boundary type safety**: Several pages cast raw API results with `as` (VoicePlatformPage 384-385/861/879/885, MatcherDebugPage:164) and `api.ts searchEntityLocation` returns `Promise<any>`. Prefer typing the `api.ts` functions concretely and narrowing, rather than casting at call sites.
- **Swallowed fetch errors**: Common pattern of `.catch(() => {})` on startup fetches (MatcherDebugPage 112-125, ConfigurationPage 442-447, VoicePlatformPage 302-303) silently degrades UI with no user signal. Watch for missing error states when reviewing data fetching.
- **Review hygiene confirmed**: dashboard has no `dangerouslySetInnerHTML`, no hardcoded secrets, all API calls use relative `/api` via the single typed `api.ts` client.
- **useActivityStream timer fix (2026-07-10, issue #133)**: Fixed post-unmount timer leak by mirroring `useCommandTraceStream`: added `retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)`, stored the ID in `onerror`, cleared it at the top of `connect()` (cancels prior retry on reconnect), and cleared it in the effect cleanup (stops pending timer on unmount). This prevents a phantom EventSource + post-unmount `setState` when the component unmounts during a backoff delay. PR #219.
- **Error UI for useQuery fetches (issue #143, 2026-05-30)**: `ResponseTemplatesPage` now destructures `isError`/`refetch` from both `useQuery` hooks and renders a styled error panel (ember border + retry button) in the template-groups section; a narrower inline banner handles command-pattern failures. `SkillOptimizerPage` had no TanStack Query — replaced the fire-and-forget `useEffect` with a `loadInit` `useCallback` tracked by `isLoadingInit`/`initError` state; a loading skeleton renders during init and a retryable error panel replaces the content area on failure. PR #190.

- Participated in 2026-05-29 health review

---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.
