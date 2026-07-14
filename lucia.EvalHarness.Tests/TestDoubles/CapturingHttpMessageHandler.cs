using System.Net;
using System.Text.Json;

namespace lucia.EvalHarness.Tests.TestDoubles;

/// <summary>
/// <see cref="HttpMessageHandler"/> that captures the outgoing request body of the first request
/// it handles and replies with a fixed, valid response. Lets the real OllamaSharp / OpenAI
/// <see cref="Microsoft.Extensions.AI.IChatClient"/> adapters run their full serialization path so
/// tests can assert on the actual JSON that would hit the wire, without a live backend.
/// </summary>
internal sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseJson;

    public CapturingHttpMessageHandler(string responseJson)
    {
        _responseJson = responseJson;
    }

    /// <summary>The raw JSON body of the last captured request.</summary>
    public string? CapturedBody { get; private set; }

    /// <summary>The parsed root element of the last captured request body.</summary>
    public JsonElement CapturedRoot { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(CapturedBody);
            CapturedRoot = doc.RootElement.Clone();
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
