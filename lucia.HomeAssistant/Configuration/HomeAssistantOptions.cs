namespace lucia.HomeAssistant.Configuration;

public class HomeAssistantOptions
{
    public const string SectionName = "HomeAssistant";

    public string BaseUrl { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public bool ValidateSSL { get; set; } = true;
}