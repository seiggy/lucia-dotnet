# New Features

## Microsoft.Agents.AI 1.0.0-preview.251204.1 → 1.0.0-preview.260212.1

This document covers new APIs and capabilities available in the updated package.

---

## Anthropic Agent Package (v251204.1)

A new agent package for Anthropic Claude models.

```csharp
using Microsoft.Agents.AI.Anthropic;

var anthropicAgent = new AnthropicAgent(new AnthropicAgentOptions
{
    ApiKey = configuration["Anthropic:ApiKey"],
    Model = "claude-3-sonnet-20240229"
});

var session = await anthropicAgent.CreateSessionAsync();
var response = await anthropicAgent.RunAsync(session, "Hello, Claude!");
```

**PR:** [#2359](https://github.com/microsoft/agent-framework/pull/2359)

---

## Cosmos DB Storage Providers (v251204.1)

Native Cosmos DB implementations for chat history and checkpoint storage.

```csharp
using Microsoft.Agents.AI.Storage.CosmosDb;

// Add Cosmos DB chat history provider
services.AddCosmosDbChatHistoryProvider(options =>
{
    options.ConnectionString = configuration["CosmosDb:ConnectionString"];
    options.DatabaseName = "agents";
    options.ContainerName = "chat-history";
});

// Add Cosmos DB checkpoint store
services.AddCosmosDbCheckpointStore(options =>
{
    options.ConnectionString = configuration["CosmosDb:ConnectionString"];
    options.DatabaseName = "agents";
    options.ContainerName = "checkpoints";
});

// With token credential (v260127.1)
services.AddCosmosDbChatHistoryProvider(
    endpoint: new Uri(configuration["CosmosDb:Endpoint"]),
    credential: new DefaultAzureCredential(),
    databaseName: "agents",
    containerName: "chat-history"
);
```

**PRs:** [#1838](https://github.com/microsoft/agent-framework/pull/1838), [#3250](https://github.com/microsoft/agent-framework/pull/3250)

---

## Declarative Agents (v251204.1)

Define agents declaratively using configuration.

```csharp
using Microsoft.Agents.AI.Declarative;

// Load agent from JSON/YAML configuration
var agent = await DeclarativeAgentLoader.LoadAsync("agent-config.json");

// Or define inline
var agentConfig = new DeclarativeAgentDefinition
{
    Name = "MyAssistant",
    Instructions = "You are a helpful assistant.",
    Model = "gpt-4",
    Tools = new[]
    {
        new ToolDefinition { Name = "search", Type = "function" }
    }
};

var agent = await DeclarativeAgentLoader.CreateAsync(agentConfig);
```

**PR:** [#2476](https://github.com/microsoft/agent-framework/pull/2476)

---

## LoggingAgent Wrapper (v251219.1)

Built-in observability wrapper using ILogger.

```csharp
using Microsoft.Agents.AI.Logging;
using Microsoft.Extensions.Logging;

ILogger<LoggingAgent> logger = loggerFactory.CreateLogger<LoggingAgent>();
AIAgent innerAgent = GetAgent();

// Wrap any agent with logging
var loggingAgent = new LoggingAgent(innerAgent, logger);

// All operations are now logged
var session = await loggingAgent.CreateSessionAsync();
var response = await loggingAgent.RunAsync(session, "Hello"); // Logs request/response
```

**PR:** [#2701](https://github.com/microsoft/agent-framework/pull/2701)

---

## Session TTL Support (v251219.1)

Configure time-to-live for durable agent sessions.

```csharp
using Microsoft.Agents.AI;

// Configure session with TTL
var sessionOptions = new AgentSessionOptions
{
    TimeToLive = TimeSpan.FromHours(24)
};

var session = await agent.CreateSessionAsync(sessionOptions);

// Session will be automatically cleaned up after 24 hours of inactivity
```

**PR:** [#2679](https://github.com/microsoft/agent-framework/pull/2679)

---

## Public A2A Agent (v260108.1)

Agent-to-Agent communication is now publicly accessible.

```csharp
using Microsoft.Agents.AI.A2A;

// Create A2A agent for inter-agent communication
var a2aAgent = new A2AAgent(new A2AAgentOptions
{
    AgentEndpoint = new Uri("https://other-agent.example.com/agent"),
    Authentication = new A2AAuthOptions { /* ... */ }
});

var session = await a2aAgent.CreateSessionAsync();
var response = await a2aAgent.RunAsync(session, "Delegate this task");
```

**PR:** [#3119](https://github.com/microsoft/agent-framework/pull/3119)

---

## GitHub Copilot SDK Agent (v260127.1)

AIAgent implementation for GitHub Copilot SDK integration.

```csharp
using Microsoft.Agents.AI.GitHub;

var copilotAgent = new GitHubCopilotAgent(new GitHubCopilotAgentOptions
{
    Token = configuration["GitHub:CopilotToken"],
    // Additional configuration
});

var session = await copilotAgent.CreateSessionAsync();
var response = await copilotAgent.RunAsync(session, "Help me with this code");
```

**PR:** [#3395](https://github.com/microsoft/agent-framework/pull/3395)

---

## Message Source Filtering (v260209.1)

Mark and filter agent request messages by source.

```csharp
using Microsoft.Agents.AI;

// Mark message source
var message = new AgentMessage("Hello")
{
    Source = MessageSource.User
};

// Filter messages by source
var userMessages = await chatHistoryProvider.GetMessagesAsync(
    agent,
    session,
    new ChatHistoryFilter { Source = MessageSource.User }
);
```

**PR:** [#3540](https://github.com/microsoft/agent-framework/pull/3540)

---

## Sealed Options and Context Classes (v251204.1)

Options and context classes are now sealed for better performance and API clarity.

```csharp
// These classes are now sealed - cannot be inherited
// ChatClientAgentOptions
// AgentRunOptions
// AgentContext
```

**PR:** [#2633](https://github.com/microsoft/agent-framework/pull/2633)

---

## Core Implementation Pattern (v260108.1, v260209.1)

New delegation pattern for custom agent implementations.

```csharp
using Microsoft.Agents.AI;

public class MyCustomAgent : AIAgent
{
    // Override core methods for custom behavior
    protected override async Task<AgentResponse> RunCoreAsync(
        AgentSession session,
        string input,
        CancellationToken cancellationToken = default)
    {
        // Custom implementation
        return new AgentResponse { Content = "Custom response" };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        AgentSession session,
        string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Custom streaming implementation
        yield return new AgentResponseUpdate { Content = "Streaming..." };
    }
    
    // Core session methods (v260209.1)
    protected override async Task<AgentSession> CreateSessionCoreAsync(
        AgentSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Custom session creation
        return new AgentSession();
    }
}
```

**PRs:** [#2749](https://github.com/microsoft/agent-framework/pull/2749), [#3699](https://github.com/microsoft/agent-framework/pull/3699)

---

## Improved Workflow Hosting (v260127.1)

Better support for hosting agents inside workflows with checkpointing.

```csharp
using Microsoft.Agents.AI.Workflows;

// Agent works correctly with workflow checkpointing
var workflow = new AgentWorkflow(agent);

// Subworkflows now work with Chat Protocol
var subWorkflow = workflow.CreateSubWorkflow("sub-task");

// Streaming resumes correctly after checkpoint restore
await foreach (var update in workflow.RunStreamingAsync(session, input))
{
    // No loss of input messages or streamed updates
}
```

**PRs:** [#3240](https://github.com/microsoft/agent-framework/pull/3240), [#3142](https://github.com/microsoft/agent-framework/pull/3142), [#2748](https://github.com/microsoft/agent-framework/pull/2748)

---

## Chat History Filtering (v260108.1)

Enhanced filtering support for chat message retrieval.

```csharp
using Microsoft.Agents.AI;

// Filter messages with various criteria
var filter = new ChatHistoryFilter
{
    FromDate = DateTime.UtcNow.AddDays(-7),
    ToDate = DateTime.UtcNow,
    MaxMessages = 100,
    Source = MessageSource.User
};

var messages = await chatHistoryProvider.GetMessagesAsync(agent, session, filter);
```

**PR:** [#2604](https://github.com/microsoft/agent-framework/pull/2604)
