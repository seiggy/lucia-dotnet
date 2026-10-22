using System.ClientModel;
using System.Net;
using System.Text;
using System.Text.Json;
using lucia.Agents.Skills;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.Providers;

/// <summary>
/// Client-side contract checks, not a running Lucia.OS server or model qualification.
/// Model metadata and errors follow seiggy/lucia.os commit bdf38e5e440debab7e9e888ea2bc1ed129deedc9:
/// InferenceRuntime.OpenAIModels, InferenceEndpoints, and HostApiKeyAuthentication.
/// Successful chat replies are synthetic OpenAI-compatible fixtures, not captured TensorSharp output.
/// </summary>
public sealed class LuciaOsInferenceContractTests
{
    private const string Model = "synthetic-chat-Q4_K_M";
    private const string InferenceKey = "synthetic-inference-only-key-00000000";
    private const string Entity = "climate.synthetic_probe";
    private const string Models = """
        {"object":"list","data":[
          {"id":"synthetic-chat-Q4_K_M","object":"model","created":0,"owned_by":"local",
           "capabilities":["chat","responses"],"backend":"ggml_cuda","context_length":16384,
           "native_context_length":32768,"max_output_tokens":4096},
          {"id":"synthetic-vector-model","object":"model","created":0,"owned_by":"local",
           "capabilities":["embedding"],"backend":"CPU","context_length":8192,"embedding_dimensions":768}
        ]}
        """;
    private const string PlainReply = """
        {"id":"chatcmpl-contract","object":"chat.completion","created":1700000000,
         "model":"synthetic-chat-Q4_K_M","choices":[{"index":0,"message":{"role":"assistant",
         "content":"The synthetic temperature is 21°C."},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":100,"completion_tokens":12,"total_tokens":112}}
        """;

    [Theory]
    [InlineData("https://lucia-os.invalid")]
    [InlineData("https://lucia-os.invalid/")]
    [InlineData("https://lucia-os.invalid/v1")]
    [InlineData("https://lucia-os.invalid/v1/")]
    public async Task DiscoveryAndChat_UseInferenceRoutesKeyAndSelectedChatModel(string endpoint)
    {
        var routes = new List<string>();
        using var handler = new DelegateHttpMessageHandler(async (request, ct) =>
        {
            AssertInferenceRequest(request);
            routes.Add(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/v1/models", request.RequestUri.AbsolutePath);
                return Reply(Models);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/chat/completions", request.RequestUri.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(Model, body.RootElement.GetProperty("model").GetString());
            Assert.Equal(128, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
            Assert.False(body.RootElement.TryGetProperty("num_predict", out _));
            return Reply(PlainReply);
        });
        using var http = new HttpClient(handler);
        var backend = Backend(endpoint);
        var models = await new BackendModelDiscovery(http).ListModelsAsync(backend);
        Assert.Equal(Model, Assert.Single(models).Name);

        using var client = new ParameterInjectingChatClient(
            BackendChatClientFactory.CreateRawClient(backend, models[0].Name, http),
            new ModelParameterProfile { Name = "contract", NumPredict = 128 });
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Read the synthetic temperature.")]);

        Assert.Equal("The synthetic temperature is 21°C.", response.Text);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(Model, response.ModelId);
        Assert.Equal(100, response.Usage?.InputTokenCount);
        Assert.Equal(12, response.Usage?.OutputTokenCount);
        Assert.Equal(["/v1/models", "/v1/chat/completions"], routes);
    }

    [Fact]
    public async Task ToolRoundTrip_PreservesRealClimateSchemaArgumentsAndFixedResultWithoutInvokingTool()
    {
        // Generate the actual skill's schema. Any attempt to invoke it fails before creating a target.
        Func<AIFunctionArguments, object> forbiddenTarget = _ => throw new InvalidOperationException("No tool execution is allowed.");
        var tool = AIFunctionFactory.Create(
            typeof(ClimateControlSkill).GetMethod(nameof(ClimateControlSkill.GetClimateStateAsync))!,
            forbiddenTarget, new AIFunctionFactoryOptions());
        var requests = 0;
        using var handler = new DelegateHttpMessageHandler(async (request, ct) =>
        {
            AssertInferenceRequest(request);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = document.RootElement;
            Assert.Equal(Model, body.GetProperty("model").GetString());
            var function = Assert.Single(body.GetProperty("tools").EnumerateArray()).GetProperty("function");
            Assert.Equal(tool.Name, function.GetProperty("name").GetString());
            Assert.Equal(tool.Description, function.GetProperty("description").GetString());
            var schema = function.GetProperty("parameters");
            Assert.Equal("string", schema.GetProperty("properties").GetProperty("entityId").GetProperty("type").GetString());
            Assert.Contains(schema.GetProperty("required").EnumerateArray(), field => field.GetString() == "entityId");
            if (++requests == 1)
            {
                return Reply(JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-tool", @object = "chat.completion", created = 1700000000, model = Model,
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = (string?)null,
                        tool_calls = new[] { new { id = "call-climate", type = "function", function = new {
                            name = tool.Name, arguments = "{\"entityId\":\"climate.synthetic_probe\"}" } } } }, finish_reason = "tool_calls" } }
                }));
            }

            Assert.Equal(2, requests);
            var messages = body.GetProperty("messages").EnumerateArray().ToArray();
            var assistant = Assert.Single(messages, message => message.GetProperty("role").GetString() == "assistant");
            var call = Assert.Single(assistant.GetProperty("tool_calls").EnumerateArray());
            Assert.Equal("call-climate", call.GetProperty("id").GetString());
            using var arguments = JsonDocument.Parse(call.GetProperty("function").GetProperty("arguments").GetString()!);
            Assert.Equal(Entity, arguments.RootElement.GetProperty("entityId").GetString());
            var result = Assert.Single(messages, message => message.GetProperty("role").GetString() == "tool");
            Assert.Equal("call-climate", result.GetProperty("tool_call_id").GetString());
            Assert.Contains("21°C", result.GetProperty("content").GetString());
            return Reply(PlainReply);
        });
        using var http = new HttpClient(handler);
        using var client = BackendChatClientFactory.CreateRawClient(Backend(), Model, http);
        var options = new ChatOptions { Tools = [tool], MaxOutputTokens = 128 };
        var history = new List<ChatMessage> { new(ChatRole.User, "Read climate.synthetic_probe, without changing it.") };
        var first = await client.GetResponseAsync(history, options);
        Assert.Equal(ChatFinishReason.ToolCalls, first.FinishReason);
        var parsedCall = Assert.Single(first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal(tool.Name, parsedCall.Name);
        Assert.Equal("call-climate", parsedCall.CallId);
        Assert.Equal(Entity, parsedCall.Arguments!["entityId"]?.ToString());

        history.AddRange(first.Messages);
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(parsedCall.CallId, "Synthetic temperature: 21°C")]));
        var final = await client.GetResponseAsync(history, options);
        Assert.Equal("The synthetic temperature is 21°C.", final.Text);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(401, "invalid_api_key")]
    [InlineData(403, "access_denied")]
    [InlineData(503, "model_not_loaded")]
    public async Task ChatErrors_PreserveHttpStatusAndHostErrorCode(int status, string code)
    {
        using var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            AssertInferenceRequest(request);
            var reply = Reply(JsonSerializer.Serialize(new { error = new { message = code,
                type = status == 503 ? "server_error" : "authentication_error", code } }), (HttpStatusCode)status);
            reply.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(reply);
        });
        using var http = new HttpClient(handler);
        using var client = BackendChatClientFactory.CreateRawClient(Backend(), Model, http);

        var error = await Assert.ThrowsAsync<ClientResultException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "Synthetic request")]));
        Assert.Equal(status, error.Status);
        Assert.Contains(code, error.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task DiscoveryErrors_DoNotBecomeAnEmptySuccessfulCatalog(int status)
    {
        using var handler = new DelegateHttpMessageHandler((_, _) => Task.FromResult(Reply("{}", (HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var discovery = new BackendModelDiscovery(http);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => discovery.ListModelsAsync(Backend()));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.False(await discovery.IsAvailableAsync(Backend()));
    }

    [Fact]
    public async Task MalformedDiscovery_ThrowsRatherThanLookingReady()
    {
        using var handler = new DelegateHttpMessageHandler((_, _) => Task.FromResult(Reply("{invalid")));
        using var http = new HttpClient(handler);
        var discovery = new BackendModelDiscovery(http);
        await Assert.ThrowsAsync<JsonException>(() => discovery.ListModelsAsync(Backend()));
        Assert.False(await discovery.IsAvailableAsync(Backend()));
    }

    [Fact]
    public async Task MalformedChat_ThrowsRatherThanReturningAnAnswer()
    {
        using var handler = new DelegateHttpMessageHandler((_, _) => Task.FromResult(Reply("{invalid")));
        using var http = new HttpClient(handler);
        using var client = BackendChatClientFactory.CreateRawClient(Backend(), Model, http);
        await Assert.ThrowsAnyAsync<JsonException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "Synthetic request")]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_PropagatesForDiscoveryAndChat(bool chat)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new DelegateHttpMessageHandler((request, ct) =>
        {
            AssertInferenceRequest(request);
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not forwarded.");
        });
        using var http = new HttpClient(handler);
        using var client = BackendChatClientFactory.CreateRawClient(Backend(), Model, http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (chat)
                await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Synthetic request")], cancellationToken: cancellation.Token);
            else
                await new BackendModelDiscovery(http).IsAvailableAsync(Backend(), cancellation.Token);
        });
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpTimeout_IsNotACompletedChatOrAvailableBackend(bool chat)
    {
        using var handler = new DelegateHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The transport must be canceled.");
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var client = BackendChatClientFactory.CreateRawClient(Backend(), Model, http);
        if (chat)
        {
            // The installed SDK retries transport timeouts and aggregates the failed attempts.
            var error = await Assert.ThrowsAsync<AggregateException>(() => client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Synthetic request")]).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotEmpty(error.Flatten().InnerExceptions);
            Assert.All(error.Flatten().InnerExceptions, attempt =>
            {
                var canceled = Assert.IsAssignableFrom<OperationCanceledException>(attempt);
                Assert.IsType<TimeoutException>(canceled.InnerException);
            });
        }
        else
            Assert.False(await new BackendModelDiscovery(http).IsAvailableAsync(Backend()).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static InferenceBackend Backend(string endpoint = "https://lucia-os.invalid") => new()
    {
        Name = "Lucia.OS contract", Type = InferenceBackendType.OpenAICompat,
        Endpoint = endpoint, ApiKey = InferenceKey
    };

    private static void AssertInferenceRequest(HttpRequestMessage request)
    {
        Assert.Equal("lucia-os.invalid", request.RequestUri!.Host);
        Assert.Contains(request.RequestUri.AbsolutePath, new[] { "/v1/models", "/v1/chat/completions" });
        Assert.Equal("Bearer " + InferenceKey, request.Headers.Authorization?.ToString());
    }

    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
