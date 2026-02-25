using System.Text.Json;
using FakeItEasy;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

public sealed class HomeAssistantTemplateClientTests
{
    [Fact]
    public async Task RunTemplateAsync_WithStringTarget_ReturnsRawString()
    {
        const string expectedTemplate = "{{ 1 + 1 }}";
        const string expectedResponse = "2";

        TemplateRenderRequest? capturedRequest = null;

        var handler = new StubHttpMessageHandler(async request =>
        {
            capturedRequest = await DeserializeRequestAsync(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(expectedResponse)
            };
        });

        var client = CreateClient(handler);
        var homeAssistantClient = (IHomeAssistantClient)client;

        var result = await homeAssistantClient.RunTemplateAsync<string>(expectedTemplate, cancellationToken: default);

        Assert.Equal(expectedResponse, result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(expectedTemplate, capturedRequest!.Template);
    }

    [Fact]
    public async Task RunTemplateAsync_WithComplexType_DeserializesResponse()
    {
        const string jinjaTemplate = "{{ {'temperature_c': 21.5, 'is_comfortable': true} | tojson }}";
        const string expectedResponse = "{\"temperature_c\":21.5,\"is_comfortable\":true}";

        TemplateRenderRequest? capturedRequest = null;

        var handler = new StubHttpMessageHandler(async request =>
        {
            capturedRequest = await DeserializeRequestAsync(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(expectedResponse)
            };
        });

        var client = CreateClient(handler);
        var homeAssistantClient = (IHomeAssistantClient)client;

        var result = await homeAssistantClient.RunTemplateAsync<ComfortResult>(jinjaTemplate);

        Assert.Equal(21.5, result.TemperatureC);
        Assert.True(result.IsComfortable);

        Assert.NotNull(capturedRequest);
        Assert.Equal(jinjaTemplate, capturedRequest!.Template);
    }    private static HomeAssistantClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = new Uri("http://localhost:8123");
        httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var monitor = A.Fake<IOptionsMonitor<HomeAssistantOptions>>();
        A.CallTo(() => monitor.CurrentValue).Returns(new HomeAssistantOptions
        {
            BaseUrl = "http://localhost:8123",
            AccessToken = "test-token"
        });

        return new HomeAssistantClient(httpClient, A.Fake<ILogger<HomeAssistantClient>>(), monitor);
    }

    private static async Task<TemplateRenderRequest> DeserializeRequestAsync(HttpRequestMessage request)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/template", request.RequestUri?.AbsolutePath);

        var content = await request.Content!.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TemplateRenderRequest>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        })!;
    }

    private sealed record ComfortResult(double TemperatureC, bool IsComfortable);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
