# Deprecations

## Microsoft.Agents.AI 1.0.0-preview.251204.1 → 1.0.0-preview.260212.1

This document covers deprecated APIs and their recommended replacements.

---

## OpenAI Assistant Client Extensions (v251219.1)

All public methods of `OpenAIAssistantClientExtensions` have been marked with `[Obsolete]`.

### Affected Methods

```csharp
// These methods are now obsolete
OpenAIAssistantClientExtensions.CreateAssistantAgentAsync(...)
OpenAIAssistantClientExtensions.GetAssistantAgentAsync(...)
OpenAIAssistantClientExtensions.DeleteAssistantAsync(...)
// ... other extension methods
```

### Migration

Use the standard AIAgent APIs instead:

**Before:**
```csharp
using Microsoft.Agents.AI.OpenAI;
using OpenAI.Assistants;

AssistantClient assistantClient = GetAssistantClient();

// Deprecated approach
var agent = await assistantClient.CreateAssistantAgentAsync(new AssistantCreationOptions
{
    Name = "My Assistant",
    Instructions = "You are helpful."
});
```

**After:**
```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient chatClient = GetChatClient();

// Recommended approach
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Instructions = "You are helpful."
});
```

**PR:** [#2640](https://github.com/microsoft/agent-framework/pull/2640)

---

## ReflectingExecutor (v260205.1)

`ReflectingExecutor` is obsolete in favor of source-generated executors.

### Reason for Deprecation

Source generation provides:
- Better performance (no runtime reflection)
- Compile-time validation
- AOT compatibility
- Smaller deployment size

### Migration

**Before:**
```csharp
using Microsoft.Agents.AI.Execution;

// Runtime reflection-based executor (deprecated)
var executor = new ReflectingExecutor(typeof(MyToolClass));

var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ToolExecutor = executor
});
```

**After:**
```csharp
using Microsoft.Agents.AI.Execution;

// Source-generated executor
[AgentTools]
public partial class MyToolClass
{
    [AgentTool]
    public string SearchDatabase(string query)
    {
        // Implementation
        return "results";
    }
    
    [AgentTool]
    public async Task<string> CallApiAsync(string endpoint)
    {
        // Implementation
        return "response";
    }
}

// Use generated executor
var executor = new MyToolClassExecutor();

var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ToolExecutor = executor
});
```

### Setting Up Source Generation

1. Add the source generator package:
   ```xml
   <PackageReference Include="Microsoft.Agents.AI.SourceGenerators" Version="1.0.0-preview.260212.1" />
   ```

2. Mark your tool class with `[AgentTools]` attribute
3. Mark individual methods with `[AgentTool]` attribute
4. Make the class `partial`
5. The generator creates `{ClassName}Executor` automatically

**PR:** [#3380](https://github.com/microsoft/agent-framework/pull/3380)

---

## Deprecation Timeline

| API | Deprecated In | Removal Target | Replacement |
|-----|---------------|----------------|-------------|
| `OpenAIAssistantClientExtensions.*` | 251219.1 | TBD | `IChatClient.AsAIAgent()` |
| `ReflectingExecutor` | 260205.1 | TBD | Source-generated executors |

---

## Handling Deprecation Warnings

### Suppress Warnings Temporarily

If you need time to migrate, you can suppress the warnings:

```csharp
#pragma warning disable CS0618 // Type or member is obsolete
var executor = new ReflectingExecutor(typeof(MyTools));
#pragma warning restore CS0618
```

Or in your project file:
```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CS0618</NoWarn>
</PropertyGroup>
```

> ⚠️ **Warning:** Suppressing warnings should only be a temporary measure. Plan to migrate before the APIs are removed.

---

## Best Practices for Migration

1. **Start with deprecation warnings enabled** to identify all usage
2. **Create migration tasks** for each deprecated API usage
3. **Test thoroughly** after migrating to new APIs
4. **Remove warning suppressions** once migration is complete
5. **Update documentation** to reflect new patterns
