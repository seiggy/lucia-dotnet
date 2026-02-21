using Microsoft.Extensions.Options;

namespace lucia.HomeAssistant.Configuration;

/// <summary>
/// Validates HomeAssistantOptions at startup to give clear error messages
/// instead of cryptic UriFormatException at runtime.
/// </summary>
public sealed class HomeAssistantOptionsValidator : IValidateOptions<HomeAssistantOptions>
{
    public ValidateOptionsResult Validate(string? name, HomeAssistantOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return ValidateOptionsResult.Fail("HomeAssistant:BaseUrl is required. Set it in appsettings.json, environment variables, or user secrets.");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return ValidateOptionsResult.Fail($"HomeAssistant:BaseUrl '{options.BaseUrl}' is not a valid HTTP/HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessToken))
        {
            return ValidateOptionsResult.Fail("HomeAssistant:AccessToken is required. Set it in appsettings.json, environment variables, or user secrets.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            return ValidateOptionsResult.Fail($"HomeAssistant:TimeoutSeconds must be positive, got {options.TimeoutSeconds}.");
        }

        return ValidateOptionsResult.Success;
    }
}
