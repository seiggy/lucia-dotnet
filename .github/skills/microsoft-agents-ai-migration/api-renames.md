# API Renames

## Microsoft.Agents.AI 1.0.0-preview.251204.1 → 1.0.0-preview.260212.1

This document provides a comprehensive reference of all type, method, and class renames.

---

## Type Renames

| Before | After | Version | PR |
|--------|-------|---------|-----|
| `AgentThread` | `AgentSession` | 260127.1 | [#3430](https://github.com/microsoft/agent-framework/pull/3430) |
| `ChatMessageStore` | `ChatHistoryProvider` | 260127.1 | [#3375](https://github.com/microsoft/agent-framework/pull/3375) |
| `AgentRunResponse` | `AgentResponse` | 260121.1 | [#3197](https://github.com/microsoft/agent-framework/pull/3197) |
| `AgentRunResponseUpdate` | `AgentResponseUpdate` | 260121.1 | [#3197](https://github.com/microsoft/agent-framework/pull/3197) |
| `AgentRunResponseEvent` | `AgentResponseEvent` | 260121.1 | [#3214](https://github.com/microsoft/agent-framework/pull/3214) |
| `AgentRunUpdateEvent` | `AgentUpdateEvent` | 260121.1 | [#3214](https://github.com/microsoft/agent-framework/pull/3214) |
| `AgentThreadMetadata` | *Removed* | 260108.1 | [#3067](https://github.com/microsoft/agent-framework/pull/3067) |

---

## Method Renames

| Class | Before | After | Version | PR |
|-------|--------|-------|---------|-----|
| `AIAgent` | `GetNewThread` | `CreateSessionAsync` | 260127.1, 260205.1 | [#3152](https://github.com/microsoft/agent-framework/pull/3152), [#3501](https://github.com/microsoft/agent-framework/pull/3501) |
| `AIAgent` | `GetNewSession` | `CreateSession` | 260205.1 | [#3501](https://github.com/microsoft/agent-framework/pull/3501) |
| `AIAgent` | `DeserializeThread` | `DeserializeSessionAsync` | 260121.1 | [#3152](https://github.com/microsoft/agent-framework/pull/3152) |
| `AgentSession` | `Serialize` | *Moved to `AIAgent.SerializeSession`* | 260205.1 | [#3650](https://github.com/microsoft/agent-framework/pull/3650) |
| Extension | `CreateAIAgent` | `AsAIAgent` | 260121.1 | [#3222](https://github.com/microsoft/agent-framework/pull/3222) |
| Extension | `GetAIAgent` | `AsAIAgent` | 260121.1 | [#3222](https://github.com/microsoft/agent-framework/pull/3222) |

---

## Casing Fixes

| Before | After | Version | PR |
|--------|-------|---------|-----|
| `GithubCopilotAgent` | `GitHubCopilotAgent` | 260128.1 | [#3486](https://github.com/microsoft/agent-framework/pull/3486) |
| `GithubCopilotAgentOptions` | `GitHubCopilotAgentOptions` | 260128.1 | [#3486](https://github.com/microsoft/agent-framework/pull/3486) |

---

## Namespace Moves

| Type | From | To | Version | PR |
|------|------|----|---------|-----|
| `TextSearchProvider` | `Microsoft.Agents.AI.Search` | `Microsoft.Agents.AI` | 251219.1 | [#2639](https://github.com/microsoft/agent-framework/pull/2639) |
| `TextSearchProviderOptions` | `Microsoft.Agents.AI.Search` | `Microsoft.Agents.AI` | 251219.1 | [#2639](https://github.com/microsoft/agent-framework/pull/2639) |

---

## Code Migration Examples

### Thread to Session Migration

**Before:**
```csharp
using Microsoft.Agents.AI;

public class MyService
{
    private readonly AIAgent _agent;
    
    public async Task ProcessAsync()
    {
        // Get new thread (sync)
        AgentThread thread = _agent.GetNewThread();
        
        // Run agent
        AgentRunResponse response = await _agent.RunAsync(thread, "Hello");
        
        // Serialize thread
        string json = thread.Serialize();
        
        // Deserialize thread (sync)
        AgentThread restored = _agent.DeserializeThread(json);
    }
}
```

**After:**
```csharp
using Microsoft.Agents.AI;

public class MyService
{
    private readonly AIAgent _agent;
    
    public async Task ProcessAsync()
    {
        // Create session (async)
        AgentSession session = await _agent.CreateSessionAsync();
        
        // Run agent
        AgentResponse response = await _agent.RunAsync(session, "Hello");
        
        // Serialize session (on agent)
        string json = _agent.SerializeSession(session);
        
        // Deserialize session (async)
        AgentSession restored = await _agent.DeserializeSessionAsync(json);
    }
}
```

---

### Extension Method Migration

**Before:**
```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient chatClient = GetChatClient();

// Multiple ways to create agent
AIAgent agent1 = chatClient.CreateAIAgent(options);
AIAgent agent2 = chatClient.GetAIAgent(options);
```

**After:**
```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient chatClient = GetChatClient();

// Single unified method
AIAgent agent = chatClient.AsAIAgent(options);
```

---

### Response Type Migration

**Before:**
```csharp
// Non-streaming
AgentRunResponse response = await agent.RunAsync(thread, input);
Console.WriteLine(response.Content);

// Streaming
await foreach (AgentRunResponseUpdate update in agent.RunStreamingAsync(thread, input))
{
    Console.Write(update.Content);
}

// Events
void HandleEvent(AgentRunResponseEvent evt) { }
void HandleUpdate(AgentRunUpdateEvent evt) { }
```

**After:**
```csharp
// Non-streaming
AgentResponse response = await agent.RunAsync(session, input);
Console.WriteLine(response.Content);

// Streaming
await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(session, input))
{
    Console.Write(update.Content);
}

// Events
void HandleEvent(AgentResponseEvent evt) { }
void HandleUpdate(AgentUpdateEvent evt) { }
```

---

### GitHub Casing Fix

**Before:**
```csharp
using Microsoft.Agents.AI.GitHub;

var options = new GithubCopilotAgentOptions
{
    // configuration
};

var agent = new GithubCopilotAgent(options);
```

**After:**
```csharp
using Microsoft.Agents.AI.GitHub;

var options = new GitHubCopilotAgentOptions
{
    // configuration
};

var agent = new GitHubCopilotAgent(options);
```

---

## Search and Replace Patterns

Use these patterns for find-and-replace operations:

| Find | Replace |
|------|---------|
| `AgentThread` | `AgentSession` |
| `ChatMessageStore` | `ChatHistoryProvider` |
| `AgentRunResponse` | `AgentResponse` |
| `AgentRunResponseUpdate` | `AgentResponseUpdate` |
| `AgentRunResponseEvent` | `AgentResponseEvent` |
| `AgentRunUpdateEvent` | `AgentUpdateEvent` |
| `GetNewThread()` | `await CreateSessionAsync()` |
| `GetNewSession()` | `CreateSession()` |
| `DeserializeThread(` | `await DeserializeSessionAsync(` |
| `.Serialize()` | Agent's `SerializeSession()` method |
| `CreateAIAgent(` | `AsAIAgent(` |
| `GetAIAgent(` | `AsAIAgent(` |
| `GithubCopilot` | `GitHubCopilot` |

> ⚠️ **Note:** Async changes require adding `await` and potentially changing method signatures to `async`. Review each change carefully.
