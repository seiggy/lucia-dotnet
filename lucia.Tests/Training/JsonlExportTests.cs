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
}
