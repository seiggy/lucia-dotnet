---
name: microsoft-agents-ai-migration
description: "Migration skill for Microsoft.Agents.AI 1.0.0-preview.251204.1 to 1.0.0-preview.260212.1: AgentThread→AgentSession, ChatMessageStore→ChatHistoryProvider, GetNewThread→CreateSession, async session methods, namespace moves, .NET 10 upgrade, and API renames."
config:
  package: Microsoft.Agents.AI
  from: 1.0.0-preview.251204.1
  to: 1.0.0-preview.260212.1
---

# Microsoft.Agents.AI Migration Guide

## 1.0.0-preview.251204.1 → 1.0.0-preview.260212.1

This migration covers significant API renames, namespace reorganization, async pattern changes, and a .NET 10 framework upgrade for the Microsoft Agent Framework AI package.

## Migration Scope

- **Session/Thread API overhaul**: `AgentThread` → `AgentSession`, synchronous methods now async
- **Storage provider renames**: `ChatMessageStore` → `ChatHistoryProvider`
- **Method renames**: `CreateAIAgent`/`GetAIAgent` → `AsAIAgent`
- **Namespace consolidation**: Classes moved to `Microsoft.Agents.AI`
- **Framework upgrade**: .NET 10 with Microsoft.Extensions.AI 10.2.0
- **Removed APIs**: Sync extension methods, unused metadata classes

## Breakdown Documentation

| Document | Description |
|----------|-------------|
| [Breaking Changes](breaking-changes.md) | All breaking changes with migration guidance |
| [API Renames](api-renames.md) | Type, method, and class renames |
| [Dependency Changes](dependency-changes.md) | Package reference and framework updates |
| [New Features](new-features.md) | New APIs and capabilities to adopt |
| [Deprecations](deprecations.md) | Deprecated APIs and replacements |

## Quick-Start Migration Checklist

- [ ] Update target framework to `net10.0`
- [ ] Update `Microsoft.Agents.AI` package to `1.0.0-preview.260212.1`
- [ ] Update Microsoft.Extensions.AI packages to `10.2.0`
- [ ] Run automated migration script: `dotnet script scripts/migrate.csx`
- [ ] Rename `AgentThread` → `AgentSession` throughout codebase
- [ ] Rename `ChatMessageStore` → `ChatHistoryProvider`
- [ ] Update `GetNewThread`/`DeserializeThread` calls to async pattern
- [ ] Replace `GetNewSession` with `CreateSession`
- [ ] Replace `CreateAIAgent`/`GetAIAgent` with `AsAIAgent`
- [ ] Fix `Github` → `GitHub` casing
- [ ] Update namespace imports for moved types
- [ ] Remove usages of deleted APIs (`NotifyThreadOfNewMessagesAsync`, `AgentThreadMetadata`, etc.)
- [ ] Replace sync extension methods with async equivalents
- [ ] Review and update `ChatHistoryProvider` method signatures (new filtering support)
- [ ] Update `AIContextProvider` and `ChatHistoryProvider` implementations to accept agent/session parameters
- [ ] Replace `ReflectingExecutor` with source-generated alternatives (if used)

## Automated Migration

Run the migration script to automatically apply renames and namespace changes:

```bash
dotnet script scripts/migrate.csx
```

> ⚠️ Review all changes before committing. The script handles common renames but may not cover all edge cases.

## Key Behavioral Changes

1. **Async Session Methods**: `GetNewThread` and `DeserializeThread` are now async—update all call sites with `await`
2. **Serialization Move**: `AgentSession.Serialize` moved to `AIAgent`—update serialization code
3. **Provider Signatures**: `AIContextProvider` and `ChatHistoryProvider` now receive agent and session context
4. **DelegatingAIAgent**: Now abstract—ensure subclasses implement required methods
5. **AIAgent.Id**: No longer allows null values

## Version Timeline

| Version | Key Changes |
|---------|-------------|
| 251204.1 | Base version, .NET 10 upgrade |
| 251219.1 | Namespace moves, deprecations, `DelegatingAIAgent` abstract |
| 260108.1 | `RunCoreAsync` pattern, `ChatMessageStore` refactor |
| 260121.1 | Async thread methods, response class renames |
| 260127.1 | `AgentThread` → `AgentSession`, `ChatMessageStore` → `ChatHistoryProvider` |
| 260205.1 | `GetNewSession` → `CreateSession`, serialization changes |
| 260209.1 | Core session methods, message source filtering |

