using System.Net;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using lucia.MusicAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

public sealed class MusicAssistantClientTests
{
    [Theory]
    [InlineData("", "", true)]
    [InlineData("https://music.example.test", "token", true)]
    [InlineData("https://music.example.test", "", false)]
    [InlineData("", "token", false)]
    [InlineData("not-a-url", "token", false)]
    public void ConfigValidator_RequiresACompleteDirectConnection(
        string url,
        string token,
        bool expectedSuccess)
    {
        var result = new MusicAssistantConfigValidator().Validate(
            null,
            new MusicAssistantConfig { Url = url, Token = token });

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    [Fact]
    public async Task TryPlayAsync_SendsAudiobookUriToMatchedPlayer()
    {
        var requests = new List<(string? Authorization, string Body)>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Headers.Authorization?.ToString(), body));
            var command = JsonDocument.Parse(body).RootElement.GetProperty("command").GetString();
            var json = command switch
            {
                "players/all" => """
                    [{"player_id":"player-office","name":"Satellite1 Dev Office","display_name":"Satellite1 Dev Office","available":true}]
                    """,
                "music/search" => """
                    {"artists":[],"albums":[],"genres":[],"tracks":[],"playlists":[],"radio":[],"audiobooks":[{"name":"Altered Carbon","uri":"library://audiobook/46","media_type":"audiobook"}],"podcasts":[],"sound_effects":[]}
                    """,
                "player_queues/play_media" => "{}",
                _ => throw new InvalidOperationException($"Unexpected command '{command}'.")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var options = A.Fake<IOptionsMonitor<MusicAssistantConfig>>();
        A.CallTo(() => options.CurrentValue).Returns(new MusicAssistantConfig
        {
            Url = "https://music.example.test",
            Token = "test-token"
        });
        var client = new MusicAssistantClient(
            httpClient,
            options,
            NullLogger<MusicAssistantClient>.Instance);

        var result = await client.TryPlayAsync(
            "Satellite1 Dev Office",
            "audiobook",
            "Altered Carbon",
            shuffle: false);

        Assert.NotNull(result);
        Assert.Equal("Altered Carbon", result.Value.MediaName);
        Assert.Equal("Satellite1 Dev Office", result.Value.PlayerName);
        Assert.All(requests, request => Assert.Equal("Bearer test-token", request.Authorization));

        using var playRequest = JsonDocument.Parse(requests[2].Body);
        var args = playRequest.RootElement.GetProperty("args");
        Assert.Equal("player-office", args.GetProperty("queue_id").GetString());
        Assert.Equal("library://audiobook/46", args.GetProperty("media").GetString());
        Assert.Equal("replace", args.GetProperty("option").GetString());
    }
}
