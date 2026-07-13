using System.Text.Json;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Evaluation;

internal sealed class ValidatingJudgeChatClient(IChatClient inner) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await inner.GetResponseAsync(messages, options, cancellationToken);
        Validate(response.Text);
        return response;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose()
    {
    }

    private static void Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException("Judge response was empty.");
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new JsonException("Judge response did not contain JSON.");
        }

        using var document = JsonDocument.Parse(text[start..(end + 1)]);
        if (!document.RootElement.TryGetProperty("score", out var scoreElement) ||
            scoreElement.ValueKind is not JsonValueKind.Number ||
            !scoreElement.TryGetDouble(out var score) ||
            !double.IsFinite(score) ||
            score is < 0 or > 100)
        {
            throw new JsonException("Judge score was missing or outside the valid range.");
        }
    }
}
