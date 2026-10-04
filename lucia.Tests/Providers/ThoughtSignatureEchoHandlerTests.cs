using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using lucia.Agents.Providers;

namespace lucia.Tests.Providers;

public sealed class ThoughtSignatureEchoHandlerTests
{
    private const string RoundOneRequest = """
        {
          "model": "test-model",
          "messages": [ { "role": "user", "content": "Turn off the air conditioner in the master bedroom" } ]
        }
        """;

    private const string RoundOneResponse = """
        {
          "id": "chatcmpl-1",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": null,
                "tool_calls": [
                  {
                    "id": "call_abc",
                    "type": "function",
                    "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\"}" },
                    "thought_signature": "SIG-A"
                  }
                ]
              },
              "finish_reason": "tool_calls"
            }
          ]
        }
        """;

    private const string RoundTwoRequest = """
        {
          "model": "test-model",
          "messages": [
            { "role": "user", "content": "Turn off the air conditioner in the master bedroom" },
            {
              "role": "assistant",
              "content": null,
              "tool_calls": [
                { "id": "call_abc", "type": "function", "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\"}" } }
              ]
            },
            { "role": "tool", "tool_call_id": "call_abc", "content": "Device updated" }
          ]
        }
        """;

    [Fact]
    public async Task CapturesSignatureFromResponseAndEchoesItInNextRound()
    {
        string? roundOneOutbound = null;
        string? roundTwoOutbound = null;
        var call = 0;
        var inner = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
            call++;
            if (call == 1)
            {
                roundOneOutbound = body;
                return JsonResponse(RoundOneResponse);
            }

            roundTwoOutbound = body;
            return JsonResponse("{}");
        });

        var client = CreateClient(inner);
        await PostAsync(client, RoundOneRequest);
        await PostAsync(client, RoundTwoRequest);

        Assert.Equal(RoundOneRequest, roundOneOutbound);
        Assert.NotNull(roundTwoOutbound);

        var assistant = (JsonObject)((JsonArray)Parse(roundTwoOutbound!)["messages"]!)[1]!;
        var toolCall = ((JsonArray)assistant["tool_calls"]!)[0] as JsonObject;
        Assert.Equal("SIG-A", (string?)toolCall?["thought_signature"]);
    }

    [Fact]
    public async Task DoesNotEchoSignatureWhenConversationPrefixDiffers()
    {
        const string otherRoundTwoRequest = """
            {
              "model": "test-model",
              "messages": [
                { "role": "user", "content": "Lock the front door" },
                {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [
                    { "id": "call_abc", "type": "function", "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\"}" } }
                  ]
                }
              ]
            }
            """;
        string? roundTwoOutbound = null;
        var call = 0;
        var inner = new FakeHttpMessageHandler(request =>
        {
            call++;
            if (call == 1)
            {
                return JsonResponse(RoundOneResponse);
            }

            roundTwoOutbound = request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
            return JsonResponse("{}");
        });

        var client = CreateClient(inner);
        await PostAsync(client, RoundOneRequest);
        await PostAsync(client, otherRoundTwoRequest);

        Assert.NotNull(roundTwoOutbound);
        var assistant = (JsonObject)((JsonArray)Parse(roundTwoOutbound!)["messages"]!)[1]!;
        var toolCall = ((JsonArray)assistant["tool_calls"]!)[0] as JsonObject;
        Assert.Null(toolCall?["thought_signature"]);
    }

    [Fact]
    public async Task FallsBackToNameAndArgumentsKeyingWhenToolCallIdIsMissing()
    {
        const string idlessRoundOneResponse = """
            {
              "id": "chatcmpl-2",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                      {
                        "type": "function",
                        "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\"}" },
                        "thought_signature": "SIG-A"
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ]
            }
            """;

        const string idlessRoundTwoRequest = """
            {
              "model": "test-model",
              "messages": [
                { "role": "user", "content": "Turn off the air conditioner in the master bedroom" },
                {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [
                    { "type": "function", "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\": \"climate.master_bedroom\"}" } }
                  ]
                },
                { "role": "tool", "tool_call_id": "functions.set_climate_device_state:0", "content": "Device updated" }
              ]
            }
            """;
        string? roundTwoOutbound = null;
        var call = 0;
        var inner = new FakeHttpMessageHandler(request =>
        {
            call++;
            if (call == 1)
            {
                return JsonResponse(idlessRoundOneResponse);
            }

            roundTwoOutbound = request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
            return JsonResponse("{}");
        });

        var client = CreateClient(inner);
        await PostAsync(client, RoundOneRequest);
        await PostAsync(client, idlessRoundTwoRequest);

        Assert.NotNull(roundTwoOutbound);
        var assistant = (JsonObject)((JsonArray)Parse(roundTwoOutbound!)["messages"]!)[1]!;
        var toolCall = ((JsonArray)assistant["tool_calls"]!)[0] as JsonObject;
        Assert.Equal("SIG-A", (string?)toolCall?["thought_signature"]);
    }

    [Fact]
    public async Task DoesNotEchoWhenArgumentsDiffer()
    {
        const string differentArgsRoundTwoRequest = """
            {
              "model": "test-model",
              "messages": [
                { "role": "user", "content": "Turn off the air conditioner in the master bedroom" },
                {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [
                    { "id": "call_abc", "type": "function", "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.kitchen\"}" } }
                  ]
                }
              ]
            }
            """;
        string? roundTwoOutbound = null;
        var call = 0;
        var inner = new FakeHttpMessageHandler(request =>
        {
            call++;
            if (call == 1)
            {
                return JsonResponse(RoundOneResponse);
            }

            roundTwoOutbound = request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
            return JsonResponse("{}");
        });

        var client = CreateClient(inner);
        await PostAsync(client, RoundOneRequest);
        await PostAsync(client, differentArgsRoundTwoRequest);

        Assert.NotNull(roundTwoOutbound);
        var assistant = (JsonObject)((JsonArray)Parse(roundTwoOutbound!)["messages"]!)[1]!;
        var toolCall = ((JsonArray)assistant["tool_calls"]!)[0] as JsonObject;
        Assert.Null(toolCall?["thought_signature"]);
    }

    [Fact]
    public async Task PassesThroughRequestsWithoutToolCallsUnchanged()
    {
        string? captured = null;
        var inner = new FakeHttpMessageHandler(request =>
        {
            captured = request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
            return JsonResponse("{}");
        });

        var client = CreateClient(inner);
        await PostAsync(client, RoundOneRequest);

        Assert.Equal(RoundOneRequest, captured);
    }

    [Fact]
    public async Task PreservesResponseContentForCallerAfterCapture()
    {
        var inner = new FakeHttpMessageHandler(_ => JsonResponse(RoundOneResponse));

        var client = CreateClient(inner);
        using var response = await SendRawAsync(client, RoundOneRequest);

        var body = await response.Content.ReadAsStringAsync();
        var parsed = Parse(body);
        var message = (JsonObject)((JsonArray)parsed["choices"]!)[0]!["message"]!;
        var toolCall = ((JsonArray)message["tool_calls"]!)[0] as JsonObject;
        Assert.Equal("SIG-A", (string?)toolCall?["thought_signature"]);
    }

    [Fact]
    public async Task CapturesNestedExtraContentSignatureAsFallback()
    {
        const string nestedRoundOneResponse = """
            {
              "id": "chatcmpl-3",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                      {
                        "id": "call_abc",
                        "type": "function",
                        "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\"}" },
                        "extra_content": { "google": { "thought_signature": "SIG-N" } }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ]
            }
            """;
        string? roundTwoOutbound = null;
        var call = 0;
        var inner = new FakeHttpMessageHandler(request =>
        {
            call++;
            if (call == 1)
            {
                return JsonResponse(nestedRoundOneResponse);
            }

            roundTwoOutbound = request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
            return JsonResponse("{}");
        });

        var client = CreateClient(inner);
        await PostAsync(client, RoundOneRequest);
        await PostAsync(client, RoundTwoRequest);

        Assert.NotNull(roundTwoOutbound);
        var assistant = (JsonObject)((JsonArray)Parse(roundTwoOutbound!)["messages"]!)[1]!;
        var toolCall = ((JsonArray)assistant["tool_calls"]!)[0] as JsonObject;
        Assert.Equal("SIG-N", (string?)toolCall?["thought_signature"]);
    }

    [Fact]
    public async Task MultiRoundLoopReinjectsEveryHistoryToolCall()
    {
        const string roundTwoResponse = """
            {
              "id": "chatcmpl-4",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                      {
                        "id": "call_def",
                        "type": "function",
                        "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\",\"state\":\"off\"}" },
                        "thought_signature": "SIG-B"
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ]
            }
            """;

        const string roundThreeRequest = """
            {
              "model": "test-model",
              "messages": [
                { "role": "user", "content": "Turn off the air conditioner in the master bedroom" },
                {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [
                    { "id": "call_abc", "type": "function", "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\"}" } }
                  ]
                },
                { "role": "tool", "tool_call_id": "call_abc", "content": "Device updated" },
                {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [
                    { "id": "call_def", "type": "function", "function": { "name": "set_climate_device_state", "arguments": "{\"entity_id\":\"climate.master_bedroom\",\"state\":\"off\"}" } }
                  ]
                },
                { "role": "tool", "tool_call_id": "call_def", "content": "Device updated" }
              ]
            }
            """;

        var outbounds = new List<string?>();
        var inner = new FakeHttpMessageHandler(request =>
        {
            outbounds.Add(request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null);
            return outbounds.Count switch
            {
                1 => JsonResponse(RoundOneResponse),
                2 => JsonResponse(roundTwoResponse),
                _ => JsonResponse("{}"),
            };
        });

        var client = CreateClient(inner);
        await PostAsync(client, RoundOneRequest);
        await PostAsync(client, RoundTwoRequest);
        await PostAsync(client, roundThreeRequest);

        Assert.Equal(3, outbounds.Count);
        var messages = (JsonArray)Parse(outbounds[2]!)["messages"]!;

        var firstCall = ((JsonArray)((JsonObject)messages[1]!)["tool_calls"]!)[0] as JsonObject;
        Assert.Equal("SIG-A", (string?)firstCall?["thought_signature"]);

        var secondCall = ((JsonArray)((JsonObject)messages[3]!)["tool_calls"]!)[0] as JsonObject;
        Assert.Equal("SIG-B", (string?)secondCall?["thought_signature"]);
    }

    [Fact]
    public void ConversationKeyIgnoresInjectedSignatures()
    {
        var plain = (JsonArray)JsonNode.Parse(
            """[{"role":"assistant","tool_calls":[{"id":"x","function":{"name":"f","arguments":"{}"}}]}]""")!;
        var signed = (JsonArray)JsonNode.Parse(
            """[{"role":"assistant","tool_calls":[{"id":"x","thought_signature":"SIG","function":{"name":"f","arguments":"{}"}}]}]""")!;

        Assert.Equal(ThoughtSignatureEchoHandler.ComputeConversationKey(plain),
            ThoughtSignatureEchoHandler.ComputeConversationKey(signed));
    }

    [Fact]
    public void ConversationKeyChangesWhenHistoryChanges()
    {
        var a = (JsonArray)JsonNode.Parse("""[{"role":"user","content":"hello"}]""")!;
        var b = (JsonArray)JsonNode.Parse("""[{"role":"user","content":"goodbye"}]""")!;

        Assert.NotEqual(ThoughtSignatureEchoHandler.ComputeConversationKey(a),
            ThoughtSignatureEchoHandler.ComputeConversationKey(b));
    }

    [Fact]
    public void ToolCallKeyCanonicalizesArgumentsAndPrefersId()
    {
        var reordered = (JsonObject)JsonNode.Parse(
            """{"function":{"name":"f","arguments":"{\"b\": 1, \"a\": 2}"}}""")!;
        var canonical = (JsonObject)JsonNode.Parse(
            """{"function":{"name":"f","arguments":"{\"a\":2,\"b\":1}"}}""")!;
        var different = (JsonObject)JsonNode.Parse(
            """{"function":{"name":"f","arguments":"{\"a\":3,\"b\":1}"}}""")!;
        var withId = (JsonObject)JsonNode.Parse("""{"id":"call_1"}""")!;

        Assert.Equal(ThoughtSignatureEchoHandler.ComputeToolCallKey(reordered),
            ThoughtSignatureEchoHandler.ComputeToolCallKey(canonical));
        Assert.NotEqual(ThoughtSignatureEchoHandler.ComputeToolCallKey(reordered),
            ThoughtSignatureEchoHandler.ComputeToolCallKey(different));
        Assert.Equal("id:call_1|name:|args:", ThoughtSignatureEchoHandler.ComputeToolCallKey(withId));
    }

    private static HttpClient CreateClient(FakeHttpMessageHandler inner) =>
        new(new ThoughtSignatureEchoHandler(new ThoughtSignatureStore(TimeProvider.System))
        {
            InnerHandler = inner,
        });

    private static async Task PostAsync(HttpClient client, string body)
    {
        using var response = await SendRawAsync(client, body);
        Assert.True(response.IsSuccessStatusCode);
    }

    private static async Task<HttpResponseMessage> SendRawAsync(HttpClient client, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        return await client.SendAsync(request);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;
}
