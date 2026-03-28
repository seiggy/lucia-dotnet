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
