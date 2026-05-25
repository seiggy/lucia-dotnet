using System.Net;
using System.Net.Http.Json;
using System.Text;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class HomeAssistantShoppingListFallbackTests
{
    [Fact]
    public async Task GetShoppingListItemsAsync_ReturnsNativeShoppingListItems_WhenEndpointExists()
    {
        var requests = new List<string>();
        var client = CreateClient(request =>
        {
            requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");

            if (request.Method == HttpMethod.Get && request.RequestUri!.PathAndQuery == "/api/shopping_list")
            {
                return JsonResponse("""
                    [
                      { "id": "native-1", "name": "Milk", "complete": false }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var items = await client.GetShoppingListItemsAsync();

        var item = Assert.Single(items);
        Assert.Equal("native-1", item.Id);
        Assert.Equal("Milk", item.Name);
        Assert.False(item.Complete);
        Assert.Collection(
            requests,
            request => Assert.Equal("GET /api/shopping_list", request));
    }

    [Fact]
    public async Task GetShoppingListItemsAsync_FallsBackToTodoItems_WhenShoppingListEndpointReturnsNotFound()
    {
        var requests = new List<string>();
        var client = CreateClient(request =>
        {
            requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");

            if (request.Method == HttpMethod.Get && request.RequestUri!.PathAndQuery == "/api/shopping_list")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.PathAndQuery == "/api/states")
            {
                return JsonResponse("""
                    [
                      {
                        "entity_id": "todo.shopping_list",
                        "state": "1",
                        "attributes": {
                          "friendly_name": "Shopping List"
                        }
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.PathAndQuery == "/api/services/todo/get_items?return_response")
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"entity_id\":\"todo.shopping_list\"", body, StringComparison.Ordinal);

                return JsonResponse("""
                    {
                      "todo.shopping_list": {
                        "items": [
                          { "summary": "Milk", "uid": "todo-1", "status": "needs_action" },
                          { "summary": "Eggs", "uid": "todo-2", "status": "completed" }
                        ]
                      }
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var items = await client.GetShoppingListItemsAsync();

        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal("todo-1", item.Id);
                Assert.Equal("Milk", item.Name);
                Assert.False(item.Complete);
            },
            item =>
            {
                Assert.Equal("todo-2", item.Id);
                Assert.Equal("Eggs", item.Name);
                Assert.True(item.Complete);
            });

        Assert.Collection(
            requests,
            request => Assert.Equal("GET /api/shopping_list", request),
            request => Assert.Equal("GET /api/states", request),
            request => Assert.Equal("POST /api/services/todo/get_items?return_response", request));
    }

    private static HomeAssistantClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("http://homeassistant.local")
        };

        var options = new TestOptionsMonitor<HomeAssistantOptions>(new HomeAssistantOptions
        {
            BaseUrl = "http://homeassistant.local",
            AccessToken = "token"
        });

        return new HomeAssistantClient(httpClient, NullLogger<HomeAssistantClient>.Instance, options);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
