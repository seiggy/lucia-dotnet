namespace lucia.MusicAgent;

public sealed class MusicAssistantConfig
{
    public const string SectionName = "MusicAssistant";

    public string IntegrationId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
}