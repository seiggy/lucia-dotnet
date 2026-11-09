using System.Text;
using System.Text.Json;

namespace lucia.Tests.TestDoubles;

/// <summary>Replies in memory. No model server or network connection is used.</summary>
internal sealed class SyntheticModelTelemetryHandler(string model, string output) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host != "telemetry-test.invalid") throw new InvalidOperationException("Unexpected endpoint.");
        RequestCount++;
        var json = request.RequestUri.AbsolutePath.EndsWith("/embed", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(new { model, embeddings = new[] { new[] { 0.1f, 0.2f } }, prompt_eval_count = 4, total_duration = 1 })
            : JsonSerializer.Serialize(new
            {
                model, created_at = "2026-10-23T00:00:00Z", message = new { role = "assistant", content = output },
                done = true, done_reason = "stop", prompt_eval_count = 4, eval_count = 3
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json + "\n", Encoding.UTF8, "application/x-ndjson")
        });
    }
}
