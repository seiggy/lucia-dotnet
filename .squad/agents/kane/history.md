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
