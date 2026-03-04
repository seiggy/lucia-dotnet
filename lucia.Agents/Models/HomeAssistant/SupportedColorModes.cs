namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Supported color modes for light entities
/// </summary>
[Flags]
public enum SupportedColorModes
{
    None = 0,
    Brightness = 1,
    ColorTemp = 2,
    Hs = 4  // Hue/Saturation
}