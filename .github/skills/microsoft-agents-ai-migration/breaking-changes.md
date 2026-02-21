# Breaking Changes

## Microsoft.Agents.AI 1.0.0-preview.251204.1 → 1.0.0-preview.260212.1

This document details all breaking changes requiring code modifications.

---

## Session/Thread API Overhaul

### `AgentThread` → `AgentSession` (v260127.1)

The `AgentThread` class has been renamed to `AgentSession` for consistency with session-based terminology.

**Before:**
```csharp
AgentThread thread = await agent.GetNewThreadAsync();
await agent.RunAsync(thread, "Hello");
```

**After:**
```csharp
AgentSession session = await agent.CreateSessionAsync();
await agent.RunAsync(session, "Hello");
```

**PR:** [#3430](https://github.com/microsoft/agent-framework/pull/3430)

---

### `GetNewSession` → `CreateSession` (v260205.1)

Method renamed for clarity.

**Before:**
```csharp
var session = await agent.GetNewSessionAsync();
```

**After:**
```csharp
var session = await agent.CreateSessionAsync();
```

**PR:** [#3501](https://github.com/microsoft/agent-framework/pull/3501)

---

### Async Session Methods (v260121.1)

`GetNewThread` and `DeserializeThread` are now async methods.

**Before:**
```csharp
AgentThread thread = agent.GetNewThread();
AgentThread restored = agent.DeserializeThread(json);
```

**After:**
```csharp
AgentSession session = await agent.CreateSessionAsync();
AgentSession restored = await agent.DeserializeSessionAsync(json);
```

**PR:** [#3152](https://github.com/microsoft/agent-framework/pull/3152)

---

### Serialization Moved to AIAgent (v260205.1)

`AgentSession.Serialize` method moved to `AIAgent`.

**Before:**
```csharp
string json = session.Serialize();
```

**After:**
```csharp
string json = agent.SerializeSession(session);
```

**PR:** [#3650](https://github.com/microsoft/agent-framework/pull/3650)

---

## Storage Provider Changes

### `ChatMessageStore` → `ChatHistoryProvider` (v260127.1)

Storage abstraction renamed for consistency.

**Before:**
```csharp
public class MyChatStore : ChatMessageStore
{
    public override Task AddMessageAsync(ChatMessage message) { }
    public override Task<IEnumerable<ChatMessage>> GetMessagesAsync() { }
}
```

**After:**
```csharp
public class MyChatHistoryProvider : ChatHistoryProvider
{
    public override Task AddMessageAsync(AIAgent agent, AgentSession session, ChatMessage message) { }
    public override Task<IEnumerable<ChatMessage>> GetMessagesAsync(AIAgent agent, AgentSession session, ChatHistoryFilter? filter = null) { }
}
```

**PRs:** [#3375](https://github.com/microsoft/agent-framework/pull/3375), [#2604](https://github.com/microsoft/agent-framework/pull/2604), [#3695](https://github.com/microsoft/agent-framework/pull/3695)

---

### Provider Method Signatures (v260205.1)

`AIContextProvider` and `ChatHistoryProvider` methods now receive agent and session parameters.

**Before:**
```csharp
public override Task<string> GetContextAsync()
{
    return Task.FromResult("context");
}
```

**After:**
```csharp
public override Task<string> GetContextAsync(AIAgent agent, AgentSession session)
{
    return Task.FromResult($"context for session {session.Id}");
}
```

**PR:** [#3695](https://github.com/microsoft/agent-framework/pull/3695)

---

## Class/Method Renames

### Response Classes (v260121.1)

| Before | After |
|--------|-------|
| `AgentRunResponse` | `AgentResponse` |
| `AgentRunResponseUpdate` | `AgentResponseUpdate` |
| `AgentRunResponseEvent` | `AgentResponseEvent` |
| `AgentRunUpdateEvent` | `AgentUpdateEvent` |

**Before:**
```csharp
AgentRunResponse response = await agent.RunAsync(session, input);
await foreach (AgentRunResponseUpdate update in agent.RunStreamingAsync(session, input))
{
    // handle update
}
```

**After:**
```csharp
AgentResponse response = await agent.RunAsync(session, input);
await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(session, input))
{
    // handle update
}
```

**PRs:** [#3197](https://github.com/microsoft/agent-framework/pull/3197), [#3214](https://github.com/microsoft/agent-framework/pull/3214)

---

### `CreateAIAgent`/`GetAIAgent` → `AsAIAgent` (v260121.1)

Extension methods consolidated.

**Before:**
```csharp
AIAgent agent = chatClient.CreateAIAgent(options);
AIAgent agent = chatClient.GetAIAgent(options);
```

**After:**
```csharp
AIAgent agent = chatClient.AsAIAgent(options);
```

**PR:** [#3222](https://github.com/microsoft/agent-framework/pull/3222)

---

### `Github` → `GitHub` Casing Fix (v260128.1)

**Before:**
```csharp
var copilotAgent = new GithubCopilotAgent(options);
```

**After:**
```csharp
var copilotAgent = new GitHubCopilotAgent(options);
```

**PR:** [#3486](https://github.com/microsoft/agent-framework/pull/3486)

---

## AIAgent Method Changes

### `DelegatingAIAgent` Now Abstract (v251219.1)

`DelegatingAIAgent` is now abstract and requires implementation of core methods.

**Before:**
```csharp
var wrapper = new DelegatingAIAgent(innerAgent);
```

**After:**
```csharp
public class MyDelegatingAgent : DelegatingAIAgent
{
    public MyDelegatingAgent(AIAgent inner) : base(inner) { }
    
    // Implement required abstract methods
}
```

**PR:** [#2797](https://github.com/microsoft/agent-framework/pull/2797)

---

### `AIAgent.Id` Non-Nullable (v251219.1)

`AIAgent.Id` property no longer accepts null values.

**Before:**
```csharp
var agent = new MyAgent { Id = null }; // Allowed
```

**After:**
```csharp
var agent = new MyAgent { Id = "my-agent-id" }; // Required
```

**PR:** [#2719](https://github.com/microsoft/agent-framework/pull/2719)

---

### Core Implementation Methods (v260108.1, v260209.1)

New `RunCoreAsync`/`RunCoreStreamingAsync` delegation pattern introduced.

**Before:**
```csharp
public override async Task<AgentResponse> RunAsync(AgentSession session, string input)
{
    // Direct implementation
}
```

**After:**
```csharp
protected override async Task<AgentResponse> RunCoreAsync(AgentSession session, string input, CancellationToken cancellationToken)
{
    // Core implementation
}
```

**PRs:** [#2749](https://github.com/microsoft/agent-framework/pull/2749), [#3699](https://github.com/microsoft/agent-framework/pull/3699)

---

## Removed APIs

### `NotifyThreadOfNewMessagesAsync` (v251204.1)

Helper method removed. Use direct message handling instead.

**PR:** [#2450](https://github.com/microsoft/agent-framework/pull/2450)

---

### `AgentThreadMetadata` (v260108.1)

Unused class removed.

**PR:** [#3067](https://github.com/microsoft/agent-framework/pull/3067)

---

### Display Name Property (v251219.1)

Removed from agent classes.

**PR:** [#2758](https://github.com/microsoft/agent-framework/pull/2758)

---

### Sync Extension Methods (v260121.1)

All synchronous extension methods for agents have been removed. Use async equivalents.

**Before:**
```csharp
var response = agent.Run(session, "Hello");
```

**After:**
```csharp
var response = await agent.RunAsync(session, "Hello");
```

**PR:** [#3291](https://github.com/microsoft/agent-framework/pull/3291)

---

### `UserInputRequests` Property (v260205.1)

Property removed from response classes.

**PR:** [#3682](https://github.com/microsoft/agent-framework/pull/3682)

---

## Namespace Changes

### `TextSearchProvider` Moved (v251219.1)

`TextSearchProvider` and `TextSearchProviderOptions` moved to `Microsoft.Agents.AI` namespace.

**Before:**
```csharp
using Microsoft.Agents.AI.Search;

var provider = new TextSearchProvider(options);
```

**After:**
```csharp
using Microsoft.Agents.AI;

var provider = new TextSearchProvider(options);
```

**PR:** [#2639](https://github.com/microsoft/agent-framework/pull/2639)

---

### OpenAI Classes Namespace Change (v251219.1)

Classes in `Microsoft.Agents.AI.OpenAI` have reorganized namespaces.

**PR:** [#2627](https://github.com/microsoft/agent-framework/pull/2627)

---

## Constructor/Options Changes

### `ChatClientAgentOptions` Simplified (v251204.1)

Constructor and instructions handling refactored.

**PR:** [#1517](https://github.com/microsoft/agent-framework/pull/1517)

---

### CosmosDB Auth Token Support (v260127.1)

CosmosDB extensions now accept auth token credentials.

**Before:**
```csharp
services.AddCosmosDbChatHistoryProvider(connectionString);
```

**After:**
```csharp
services.AddCosmosDbChatHistoryProvider(endpoint, tokenCredential);
// or
services.AddCosmosDbChatHistoryProvider(connectionString);
```

**PR:** [#3250](https://github.com/microsoft/agent-framework/pull/3250)

---

## Declarative Object Model Changes

### Declarative Object Model Update (v260128.1)

The declarative object model and its dependencies have been updated. Review any declarative agent configurations for compatibility.

**PR:** [#3017](https://github.com/microsoft/agent-framework/pull/3017)
