# Quickstart: Multi-Agent Orchestration

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Audience**: Developers implementing or extending the orchestration feature

## Prerequisites

- .NET 10 RC1 SDK
- Docker Desktop (for Redis)
- Ollama (for local LLM testing)
- Git
- VS Code or Visual Studio 2022+

## Local Development Setup

### 1. Start Redis

```powershell
docker run -d --name lucia-redis -p 6379:6379 redis:7-alpine
```

### 2. Install Ollama and Pull Models

```powershell
# Install Ollama from https://ollama.ai
ollama pull phi3:mini
ollama pull llama3.2:3b
```

### 3. Configure Connection Strings

Edit `lucia.AppHost/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "ollama-phi3-mini": "Endpoint=http://localhost:11434;Model=phi3:mini",
    "ollama-llama3-2-3b": "Endpoint=http://localhost:11434;Model=llama3.2:3b",
    "redis": "localhost:6379"
  }
}
```

### 4. Configure RouterExecutor

Edit `lucia.AgentHost/appsettings.Development.json`:

```json
{
  "RouterExecutor": {
    "ChatClientKey": "ollama-phi3-mini",
    "ConfidenceThreshold": 0.7,
    "SystemPrompt": "You are a routing assistant...",
    "UserPromptTemplate": "Given this request: {0}\n\nSelect agent..."
  },
  "AgentExecutorWrapper": {
    "DefaultTimeoutMs": 30000,
    "MaxRetries": 2
  },
  "ResultAggregator": {
    "FallbackMessage": "I encountered issues processing your request.",
    "SuccessTemplate": "I've completed {0} action(s): {1}"
  }
}
```

## Build and Run

```powershell
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run AppHost (starts AgentHost, ServiceDefaults, observability)
dotnet run --project lucia.AppHost
```

Access dashboard at: http://localhost:5000

## First Agent Implementation

### 1. Create Agent Class

Create `lucia.Agents/Agents/WeatherAgent.cs`:

```csharp
namespace lucia.Agents.Agents;

[AgentCard(
    Id = "weather-agent",
    Name = "Weather Agent",
    Description = "Provides weather information and forecasts",
    Capabilities = ["weather.current", "weather.forecast"])]
public sealed class WeatherAgent : AIAgent
{
    public WeatherAgent(
        IChatClient chatClient,
        ILogger<WeatherAgent> logger)
        : base(chatClient, logger)
    {
        Name = "WeatherAgent";
        Instructions = "You provide weather information based on user location.";
    }
    
    [Skill(
        Name = "GetCurrentWeather",
        Description = "Get current weather for a location",
        Examples = ["What's the weather in Seattle?", "Is it raining?"])]
    public async Task<string> GetCurrentWeatherAsync(
        [Description("City name")] string city,
        CancellationToken cancellationToken)
    {
        // Integrate with weather API
        return $"The current weather in {city} is sunny, 72°F.";
    }
}
```

### 2. Register Agent

Edit `lucia.AgentHost/Program.cs`:

```csharp
builder.Services.AddSingleton<WeatherAgent>();
builder.Services.AddHostedService<AgentRegistrationService>();
```

### 3. Test Agent Registration

```powershell
# Start AppHost
dotnet run --project lucia.AppHost

# Check agent registry endpoint
curl http://localhost:5173/api/agents
```

Expected response:
```json
{
  "agents": [
    {
      "id": "weather-agent",
      "name": "Weather Agent",
      "description": "Provides weather information and forecasts",
      "capabilities": ["weather.current", "weather.forecast"]
    }
  ]
}
```

## Testing Workflow

### Unit Tests

```csharp
[Fact]
public async Task RouterExecutor_SelectsWeatherAgent_ForWeatherQuery()
{
    // Arrange
    var chatClient = A.Fake<IChatClient>();
    var registry = A.Fake<AgentRegistry>();
    var options = Options.Create(new RouterExecutorOptions
    {
        ChatClientKey = "test-client",
        ConfidenceThreshold = 0.7
    });
    
    A.CallTo(() => registry.GetAllAgentsAsync(A<CancellationToken>._))
        .Returns(new[] { /* weather agent card */ });
    
    var executor = new RouterExecutor(chatClient, registry, logger, options);
    var message = new ChatMessage(ChatRole.User, "What's the weather in Seattle?");
    
    // Act
    var result = await executor.HandleAsync(message, context, CancellationToken.None);
    
    // Assert
    Assert.Equal("weather-agent", result.AgentId);
    Assert.True(result.Confidence >= 0.7);
}
```

### Integration Tests

```csharp
[Fact]
public async Task LuciaOrchestrator_ExecutesWeatherAgent_ReturnsResult()
{
    // Arrange
    using var app = await DistributedApplicationTestingBuilder
        .CreateAsync<Projects.lucia_AppHost>();
    await app.StartAsync();
    
    var httpClient = app.CreateHttpClient("agent-host");
    
    // Act
    var response = await httpClient.PostAsJsonAsync("/api/orchestrate", new
    {
        Message = "What's the weather in Seattle?",
        SessionId = "test-session",
        TaskId = "test-task"
    });
    
    // Assert
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<OrchestrationResult>();
    Assert.Contains("weather", result.Response, StringComparison.OrdinalIgnoreCase);
}
```

Run tests:
```powershell
dotnet test
```

## Debugging Tips

### Enable Verbose Logging

```json
{
  "Logging": {
    "LogLevel": {
      "lucia.Agents.Orchestration": "Debug",
      "Microsoft.Agents": "Information"
    }
  }
}
```

### View Telemetry

1. Navigate to Aspire dashboard: http://localhost:5000
2. Select "Traces" → Filter by `LuciaOrchestrator`
3. Inspect span timeline for workflow execution

### Common Issues

**Issue**: RouterExecutor returns low confidence scores  
**Solution**: Check `SystemPrompt` clarity, ensure agent capabilities well-defined

**Issue**: Agent timeout exceeded  
**Solution**: Increase `DefaultTimeoutMs` in `AgentExecutorWrapperOptions`

**Issue**: Redis connection failed  
**Solution**: Verify Docker container running: `docker ps | grep lucia-redis`

**Issue**: Ollama model not found  
**Solution**: Pull model: `ollama pull phi3:mini`

## Next Steps

1. Review [data-model.md](./data-model.md) for entity schemas
2. Review contract documents in `contracts/` for implementation details
3. Run Phase 2: `follow instructions in speckit.plan.prompt.md` to generate task breakdown
4. Implement tasks using Test-First Development (TDD)

## Resources

- [Microsoft Agent Framework Docs](https://github.com/microsoft/agent-framework)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire)
- [Redis Documentation](https://redis.io/docs)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/net)
