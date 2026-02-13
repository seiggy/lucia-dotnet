namespace lucia.HomeAssistant.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class HomeAssistantApiAttribute : Attribute
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ConfigSectionName { get; set; } = "HomeAssistant";
}