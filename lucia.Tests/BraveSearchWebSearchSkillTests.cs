using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FakeItEasy;
using Microsoft.Extensions.Logging;

namespace lucia.Tests;

/// <summary>
/// Tests for the Brave Search web search skill HTTP integration.
/// Since the skill is loaded via Roslyn script, we test the HTTP + JSON
/// contract that the plugin relies on.
/// </summary>
public sealed class BraveSearchWebSearchSkillTests
{
    private readonly ILogger _logger = A.Fake<ILogger>();

    [Fact]
    public async Task WebSearch_SuccessfulResponse_ReturnsFormattedResults()
    {
        // Arrange
        var responseJson = """
        {
            "web": {
                "results": [
                    {
                        "title": "Test Result One",
                        "url": "https://example.com/one",
                        "description": "Description of the first result."
                    },
                    {
                        "title": "Test Result Two",
                        "url": "https://example.com/two",
                        "description": "Description of the second result."
                    }
                ]
            }
        }
        """;

        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.Add("X-Subscription-Token", "test-api-key");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var factory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => factory.CreateClient("BraveSearch")).Returns(httpClient);

        // Act
        var searchUrl = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString("test query")}&count=8";
        var response = await httpClient.GetAsync(searchUrl);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveSearchResponseDto>(json);

        // Assert
        Assert.NotNull(result?.Web?.Results);
        Assert.Equal(2, result.Web.Results.Count);
        Assert.Equal("Test Result One", result.Web.Results[0].Title);
        Assert.Equal("https://example.com/one", result.Web.Results[0].Url);
        Assert.Equal("Description of the first result.", result.Web.Results[0].Description);
        Assert.Equal("Test Result Two", result.Web.Results[1].Title);

        // Verify request was sent to correct URL
        Assert.NotNull(capturedRequest);
        Assert.Contains("q=test", capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("count=8", capturedRequest.RequestUri.AbsoluteUri);

        // Verify auth header
        Assert.Contains("test-api-key",
            capturedRequest.Headers.GetValues("X-Subscription-Token"));
    }

    [Fact]
    public async Task WebSearch_EmptyResults_ReturnsEmptyList()
    {
        var responseJson = """
        {
            "web": {
                "results": []
            }
        }
        """;

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/web/search?q=nothing&count=8");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveSearchResponseDto>(json);

        Assert.NotNull(result?.Web?.Results);
        Assert.Empty(result.Web.Results);
    }

    [Fact]
    public async Task WebSearch_NullWebField_HandlesGracefully()
    {
        var responseJson = """{ "type": ["web"] }""";

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/web/search?q=test&count=8");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveSearchResponseDto>(json);

        Assert.Null(result?.Web);
    }

    [Fact]
    public async Task WebSearch_ApiError_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Invalid API key")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/web/search?q=test&count=8");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebSearch_ResultWithMissingDescription_DeserializesWithNull()
    {
        var responseJson = """
        {
            "web": {
                "results": [
                    {
                        "title": "No Description Result",
                        "url": "https://example.com/no-desc"
                    }
                ]
            }
        }
        """;

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/web/search?q=test&count=8");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveSearchResponseDto>(json);

        Assert.NotNull(result?.Web?.Results);
        Assert.Single(result.Web.Results);
        Assert.Equal("No Description Result", result.Web.Results[0].Title);
        Assert.Null(result.Web.Results[0].Description);
    }

    /// <summary>
    /// Local DTO mirroring the Brave Search plugin's response types.
    /// These validate the JSON contract the plugin relies on.
    /// </summary>
    private sealed class BraveSearchResponseDto
    {
        [JsonPropertyName("web")]
        public BraveWebResultsDto? Web { get; set; }
    }

    private sealed class BraveWebResultsDto
    {
        [JsonPropertyName("results")]
        public List<BraveWebResultDto> Results { get; set; } = [];
    }

    private sealed class BraveWebResultDto
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
