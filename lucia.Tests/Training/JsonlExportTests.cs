using System.Text.Json;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;

namespace lucia.Tests.Training;

public sealed class JsonlExportTests
{
    [Fact]
    public void ConvertTraceToJsonl_ProducesValidJson()
    {
        var trace = CreateSampleTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false);

        // Should not throw
        var doc = JsonDocument.Parse(line);
        Assert.NotNull(doc);
    }

    [Fact]
    public void ConvertTraceToJsonl_ContainsMessagesArray()
    {
        var trace = CreateSampleTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false);
        var doc = JsonDocument.Parse(line);

        Assert.True(doc.RootElement.TryGetProperty("messages", out var messages));
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.True(messages.GetArrayLength() > 0);
    }

    [Fact]
    public void ConvertTraceToJsonl_MessagesHaveRoleAndContent()
    {
        var trace = CreateSampleTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false);
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        foreach (var msg in messages.EnumerateArray())
        {
            Assert.True(msg.TryGetProperty("role", out _), "Message missing 'role' field");
            Assert.True(msg.TryGetProperty("content", out _), "Message missing 'content' field");
        }
    }

    [Fact]
    public void ConvertTraceToJsonl_UserInputAppearsInUserRole()
    {
        var trace = CreateSampleTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false);
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        var hasUserMessage = false;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "user")
            {
                Assert.Equal("Turn on the living room lights", msg.GetProperty("content").GetString());
                hasUserMessage = true;
            }
        }

        Assert.True(hasUserMessage, "No user role message found");
    }

    [Fact]
    public void ConvertTraceToJsonl_FinalResponseAppearsInAssistantRole()
    {
        var trace = CreateSampleTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false);
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        var hasAssistantMessage = false;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "assistant")
            {
                Assert.Equal("Done! Living room lights are now on.", msg.GetProperty("content").GetString());
                hasAssistantMessage = true;
            }
        }

        Assert.True(hasAssistantMessage, "No assistant role message found");
    }

    [Fact]
    public void ConvertTraceToJsonl_SystemMessagesIncluded()
    {
        var trace = CreateSampleTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false);
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        var hasSystemMessage = false;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "system")
            {
                Assert.Equal("You are a home assistant agent.", msg.GetProperty("content").GetString());
                hasSystemMessage = true;
            }
        }

        Assert.True(hasSystemMessage, "No system role message found");
    }

    [Fact]
    public void ConvertTraceToJsonl_WithCorrection_UsesCorrectionsAsAssistantContent()
    {
        var trace = CreateSampleTrace();
        trace.Label.Status = LabelStatus.Negative;
        trace.Label.CorrectionText = "I've turned on the living room lights for you.";

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: true);
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        string? assistantContent = null;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "assistant")
            {
                assistantContent = msg.GetProperty("content").GetString();
            }
        }

        Assert.Equal("I've turned on the living room lights for you.", assistantContent);
    }

    [Fact]
    public void ConvertTraceToJsonl_WithCorrection_Disabled_UsesFinalResponse()
    {
        var trace = CreateSampleTrace();
        trace.Label.Status = LabelStatus.Negative;
        trace.Label.CorrectionText = "Corrected text";

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false);
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        string? assistantContent = null;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "assistant")
            {
                assistantContent = msg.GetProperty("content").GetString();
            }
        }

        Assert.Equal("Done! Living room lights are now on.", assistantContent);
    }

    [Fact]
    public void ConvertTraceToJsonl_WithAgentFilter_OnlyIncludesMatchingAgent()
    {
        var trace = CreateMultiAgentTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false, agentFilter: "light-agent");
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        var systemMessages = new List<string>();
        string? assistantContent = null;
        foreach (var msg in messages.EnumerateArray())
        {
            var role = msg.GetProperty("role").GetString();
            if (role == "system")
                systemMessages.Add(msg.GetProperty("content").GetString()!);
            if (role == "assistant")
                assistantContent = msg.GetProperty("content").GetString();
        }

        Assert.Single(systemMessages);
        Assert.Equal("You control lights.", systemMessages[0]);
        Assert.Equal("Lights turned on.", assistantContent);
    }

    [Fact]
    public void ConvertTraceToJsonl_WithAgentFilter_IncludesToolCalls()
    {
        var trace = CreateMultiAgentTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false, agentFilter: "light-agent");
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        var hasToolMessage = false;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "tool")
            {
                hasToolMessage = true;
                Assert.Contains("toggle_light", msg.GetProperty("content").GetString()!);
            }
        }

        Assert.True(hasToolMessage, "Expected tool call messages for filtered agent");
    }

    [Fact]
    public void ConvertTraceToJsonl_WithNoMatchingAgent_FallsBackToFinalResponse()
    {
        var trace = CreateMultiAgentTrace();

        var line = JsonlConverter.ConvertTraceToJsonl(trace, includeCorrections: false, agentFilter: "nonexistent-agent");
        var doc = JsonDocument.Parse(line);
        var messages = doc.RootElement.GetProperty("messages");

        string? assistantContent = null;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "assistant")
                assistantContent = msg.GetProperty("content").GetString();
        }

        Assert.Equal("Lights on and music playing.", assistantContent);
    }

    private static ConversationTrace CreateSampleTrace()
    {
        var trace = new ConversationTrace
        {
            SessionId = "session-1",
            UserInput = "Turn on the living room lights",
            FinalResponse = "Done! Living room lights are now on.",
            TotalDurationMs = 150.0
        };

        trace.Label.Status = LabelStatus.Positive;

        var execution = new AgentExecutionRecord
        {
            AgentId = "home-agent",
            ResponseContent = "Done! Living room lights are now on.",
            Success = true,
            ExecutionDurationMs = 100.0
        };

        execution.Messages.Add(new TracedMessage
        {
            Role = "system",
            Content = "You are a home assistant agent."
        });

        execution.Messages.Add(new TracedMessage
        {
            Role = "user",
            Content = "Turn on the living room lights"
        });

        trace.AgentExecutions.Add(execution);

        return trace;
    }

    private static ConversationTrace CreateMultiAgentTrace()
    {
        var trace = new ConversationTrace
        {
            SessionId = "session-multi",
            UserInput = "Turn on the lights and play some music",
            FinalResponse = "Lights on and music playing.",
            TotalDurationMs = 250.0
        };

        trace.Label.Status = LabelStatus.Positive;

        var lightExecution = new AgentExecutionRecord
        {
            AgentId = "light-agent",
            ResponseContent = "Lights turned on.",
            Success = true,
            ExecutionDurationMs = 80.0
        };
        lightExecution.Messages.Add(new TracedMessage { Role = "system", Content = "You control lights." });
        lightExecution.Messages.Add(new TracedMessage { Role = "assistant", Content = "Lights turned on." });
        lightExecution.ToolCalls.Add(new TracedToolCall { ToolName = "toggle_light", Result = "success" });
        trace.AgentExecutions.Add(lightExecution);

        var musicExecution = new AgentExecutionRecord
        {
            AgentId = "music-agent",
            ResponseContent = "Music playing.",
            Success = true,
            ExecutionDurationMs = 120.0
        };
        musicExecution.Messages.Add(new TracedMessage { Role = "system", Content = "You play music." });
        musicExecution.Messages.Add(new TracedMessage { Role = "assistant", Content = "Music playing." });
        trace.AgentExecutions.Add(musicExecution);

        return trace;
    }
}
