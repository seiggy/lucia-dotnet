using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using lucia.Agents.Providers;

namespace lucia.Tests.Providers;

public sealed class ToolSchemaCompatHandlerTests
{
    private const string LockToolBody = """
        {
          "model": "test-model",
          "messages": [ { "role": "user", "content": "Lock the front door" } ],
          "tools": [
            {
              "type": "function",
              "function": {
                "name": "set_lock_state",
                "parameters": {
                  "type": "object",
                  "properties": {
                    "state": { "type": ["string", "null"], "enum": ["locked", "unlocked"], "maxLength": 10 },
                    "entity_id": { "type": "string" }
                  },
                  "required": ["state"]
                }
              }
            }
          ]
        }
        """;

    private static JsonObject StateProperty(string body)
    {
        var doc = (JsonObject)JsonNode.Parse(body)!;
        var function = (JsonObject)((JsonObject)doc["tools"]![0]!)["function"]!;
        var parameters = (JsonObject)function["parameters"]!;
        var properties = (JsonObject)parameters["properties"]!;
        return (JsonObject)properties["state"]!;
    }

    [Fact]
    public void NormalizesNullableTypeArrayToAnyOfPreservingKeywords()
    {
        var result = ToolSchemaCompatHandler.NormalizeToolSchemas(LockToolBody);

        Assert.NotNull(result);
        var state = StateProperty(result!);
        Assert.Null(state["type"]);

        var anyOf = (JsonArray)state["anyOf"]!;
        Assert.Equal(2, anyOf.Count);

        var stringBranch = (JsonObject)anyOf[0]!;
        Assert.Equal("string", (string?)stringBranch["type"]);
        Assert.Equal(["locked", "unlocked"],
            ((JsonArray)stringBranch["enum"]!).Select(e => (string)e!).ToArray());
        Assert.Equal(10, (int?)stringBranch["maxLength"]);

        var nullBranch = (JsonObject)anyOf[1]!;
        Assert.Equal("null", (string?)nullBranch["type"]);
        Assert.Null(nullBranch["enum"]);
        Assert.Null(nullBranch["maxLength"]);
    }

    [Fact]
    public void ReturnsNullWhenDocumentHasNoTypeArrays()
    {
        var body = """
            {
              "model": "test-model",
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "set_lock_state",
                    "parameters": {
                      "type": "object",
                      "properties": { "state": { "type": "string", "enum": ["locked"] } },
                      "required": ["state"]
                    }
                  }
                }
              ]
            }
            """;

        Assert.Null(ToolSchemaCompatHandler.NormalizeToolSchemas(body));
    }

    [Fact]
    public void RewritesNestedPropertiesAndArrayItems()
    {
        var body = """
            {
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "configure",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "device": {
                          "type": ["object", "null"],
                          "properties": {
                            "temperature": { "type": ["number", "null"], "minimum": 0, "maximum": 40 }
                          }
                        },
                        "tags": { "type": "array", "items": { "type": ["string", "null"] } }
                      }
                    }
                  }
                }
              ]
            }
            """;

        var result = ToolSchemaCompatHandler.NormalizeToolSchemas(body);

        Assert.NotNull(result);
        var doc = (JsonObject)JsonNode.Parse(result!)!;
        var parameters = (JsonObject)((JsonObject)((JsonObject)doc["tools"]![0]!)["function"]!)["parameters"]!;
        var properties = (JsonObject)parameters["properties"]!;

        var device = (JsonObject)properties["device"]!;
        Assert.NotNull(device["anyOf"]);
        var deviceBranch = (JsonObject)((JsonArray)device["anyOf"]!)[0]!;
        var temperature = (JsonObject)((JsonObject)deviceBranch["properties"]!)["temperature"]!;
        var tempAnyOf = (JsonArray)temperature["anyOf"]!;
        Assert.Equal("number", (string?)((JsonObject)tempAnyOf[0]!)["type"]);
        Assert.Equal(0, (int?)((JsonObject)tempAnyOf[0]!)["minimum"]);
        Assert.Equal(40, (int?)((JsonObject)tempAnyOf[0]!)["maximum"]);

        var tags = (JsonObject)properties["tags"]!;
        var items = (JsonObject)tags["items"]!;
        var itemsAnyOf = (JsonArray)items["anyOf"]!;
        Assert.Equal("string", (string?)((JsonObject)itemsAnyOf[0]!)["type"]);
    }

    [Fact]
    public async Task HandlerRewritesOutgoingToolBodies()
    {
        string? captured = null;
        var inner = new FakeHttpMessageHandler(request =>
        {
            captured = request.Content is not null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(new ToolSchemaCompatHandler { InnerHandler = inner });

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions")
        {
            Content = new StringContent(LockToolBody, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.NotNull(captured);
        var state = StateProperty(captured!);
        Assert.NotNull(state["anyOf"]);
        Assert.Null(state["type"]);
    }

    [Fact]
    public async Task HandlerPassesThroughBodiesWithoutTools()
    {
        const string body = """
            { "model": "test-model", "messages": [ { "role": "user", "content": "hello" } ] }
            """;
        string? captured = null;
        var inner = new FakeHttpMessageHandler(request =>
        {
            captured = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        });

        var client = new HttpClient(new ToolSchemaCompatHandler { InnerHandler = inner });

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        await client.SendAsync(request);

        Assert.Equal(body, captured);
    }
}
