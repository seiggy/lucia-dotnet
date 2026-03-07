using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FakeItEasy;
using Microsoft.Extensions.Logging;

namespace lucia.Tests;

/// <summary>
/// Tests for the Brave Search web search skill HTTP integration.
/// Since the skill is loaded via Roslyn script, we test the HTTP + JSON
/// contract that the plugin relies on (Brave LLM Context API).
/// </summary>
public sealed class BraveSearchWebSearchSkillTests
{
    private readonly ILogger _logger = A.Fake<ILogger>();

    [Fact]
    public async Task WebSearch_SuccessfulResponse_DeserializesAndValidatesRequest()
    {
        // Arrange
        var responseJson = """
        {
            "grounding": {
                "generic": [
                    {
                        "url": "https://example.com/one",
                        "title": "Test Result One",
                        "snippets": [
                            "First relevant text chunk from the page.",
                            "Second relevant passage from the same page."
                        ]
                    },
                    {
                        "url": "https://example.com/two",
                        "title": "Test Result Two",
                        "snippets": [
                            "Content extracted from the second source."
                        ]
                    }
                ]
            },
            "sources": {
                "https://example.com/one": {
                    "title": "Test Result One",
                    "hostname": "example.com",
                    "age": ["Monday, January 15, 2024", "2024-01-15", "380 days ago"]
                },
                "https://example.com/two": {
                    "title": "Test Result Two",
                    "hostname": "example.com",
                    "age": null
                }
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

        // Act
        var searchUrl = $"https://api.search.brave.com/res/v1/llm/context?q={Uri.EscapeDataString("test query")}&count=8&maximum_number_of_tokens=8192";
        var response = await httpClient.GetAsync(searchUrl);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveLlmContextResponseDto>(json);

        // Assert
        Assert.NotNull(result?.Grounding?.Generic);
        Assert.Equal(2, result.Grounding.Generic.Count);
        Assert.Equal("Test Result One", result.Grounding.Generic[0].Title);
        Assert.Equal("https://example.com/one", result.Grounding.Generic[0].Url);
        Assert.Equal(2, result.Grounding.Generic[0].Snippets.Count);
        Assert.Equal("First relevant text chunk from the page.", result.Grounding.Generic[0].Snippets[0]);
        Assert.Equal("Test Result Two", result.Grounding.Generic[1].Title);
        Assert.Single(result.Grounding.Generic[1].Snippets);

        // Verify request was sent to correct LLM Context API URL
        Assert.NotNull(capturedRequest);
        Assert.Contains("/v1/llm/context", capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("q=test", capturedRequest.RequestUri.AbsoluteUri);
        Assert.Contains("count=8", capturedRequest.RequestUri.AbsoluteUri);
        Assert.Contains("maximum_number_of_tokens=8192", capturedRequest.RequestUri.AbsoluteUri);

        // Verify auth header
        Assert.Contains("test-api-key",
            capturedRequest.Headers.GetValues("X-Subscription-Token"));
    }

    [Fact]
    public async Task WebSearch_EmptyGrounding_ReturnsEmptyList()
    {
        var responseJson = """
        {
            "grounding": {
                "generic": []
            },
            "sources": {}
        }
        """;

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/llm/context?q=nothing&count=8&maximum_number_of_tokens=8192");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveLlmContextResponseDto>(json);

        Assert.NotNull(result?.Grounding?.Generic);
        Assert.Empty(result.Grounding.Generic);
    }

    [Fact]
    public async Task WebSearch_NullGroundingField_HandlesGracefully()
    {
        var responseJson = """{ "sources": {} }""";

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/llm/context?q=test&count=8&maximum_number_of_tokens=8192");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveLlmContextResponseDto>(json);

        Assert.Null(result?.Grounding);
    }

    [Fact]
    public async Task WebSearch_ApiError_ReturnsUnauthorizedStatus()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Invalid API key")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/llm/context?q=test&count=8&maximum_number_of_tokens=8192");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebSearch_GroundingItemWithEmptySnippets_DeserializesCorrectly()
    {
        var responseJson = """
        {
            "grounding": {
                "generic": [
                    {
                        "url": "https://example.com/no-snippets",
                        "title": "No Snippets Result",
                        "snippets": []
                    }
                ]
            },
            "sources": {
                "https://example.com/no-snippets": {
                    "title": "No Snippets Result",
                    "hostname": "example.com",
                    "age": null
                }
            }
        }
        """;

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/llm/context?q=test&count=8&maximum_number_of_tokens=8192");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveLlmContextResponseDto>(json);

        Assert.NotNull(result?.Grounding?.Generic);
        Assert.Single(result.Grounding.Generic);
        Assert.Equal("No Snippets Result", result.Grounding.Generic[0].Title);
        Assert.Empty(result.Grounding.Generic[0].Snippets);
    }

    [Fact]
    public async Task WebSearch_SourceMetadata_DeserializesCorrectly()
    {
        var responseJson = """
        {
            "grounding": {
                "generic": [
                    {
                        "url": "https://example.com/page",
                        "title": "Example Page",
                        "snippets": ["Some content from the page."]
                    }
                ]
            },
            "sources": {
                "https://example.com/page": {
                    "title": "Example Page",
                    "hostname": "example.com",
                    "age": ["Monday, January 15, 2024", "2024-01-15", "380 days ago"]
                }
            }
        }
        """;

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var response = await httpClient.GetAsync("https://api.search.brave.com/res/v1/llm/context?q=test&count=8&maximum_number_of_tokens=8192");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BraveLlmContextResponseDto>(json);

        Assert.NotNull(result?.Sources);
        Assert.True(result.Sources.ContainsKey("https://example.com/page"));
        var source = result.Sources["https://example.com/page"];
        Assert.Equal("Example Page", source.Title);
        Assert.Equal("example.com", source.Hostname);
        Assert.NotNull(source.Age);
        Assert.Equal(3, source.Age.Count);
    }

    /// <summary>
    /// Local DTOs mirroring the Brave LLM Context API response types.
    /// These validate the JSON contract the plugin relies on.
    /// </summary>
    private sealed class BraveLlmContextResponseDto
    {
        [JsonPropertyName("grounding")]
        public BraveLlmGroundingDto? Grounding { get; set; }

        [JsonPropertyName("sources")]
        public Dictionary<string, BraveLlmSourceDto>? Sources { get; set; }
    }

    private sealed class BraveLlmGroundingDto
    {
        [JsonPropertyName("generic")]
        public List<BraveLlmGroundingItemDto> Generic { get; set; } = [];
    }

    private sealed class BraveLlmGroundingItemDto
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("snippets")]
        public List<string> Snippets { get; set; } = [];
    }

    private sealed class BraveLlmSourceDto
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = "";

        [JsonPropertyName("age")]
        public List<string>? Age { get; set; }
    }
}
