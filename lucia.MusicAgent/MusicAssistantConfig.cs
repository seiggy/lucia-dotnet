using System.ComponentModel.DataAnnotations;

namespace lucia.MusicAgent;

public sealed class MusicAssistantConfig
{
    public string IntegrationId { get; set; } = string.Empty;
}