using Microsoft.Extensions.Options;

namespace lucia.MusicAgent;

public sealed class MusicAssistantConfigValidator : IValidateOptions<MusicAssistantConfig>
{
    public ValidateOptionsResult Validate(string? name, MusicAssistantConfig options)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(options.Url);
        var hasToken = !string.IsNullOrWhiteSpace(options.Token);

        if (hasUrl != hasToken)
        {
            return ValidateOptionsResult.Fail(
                "MusicAssistant Url and Token must either both be configured or both be empty.");
        }

        if (hasUrl
            && (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")))
        {
            return ValidateOptionsResult.Fail(
                "MusicAssistant Url must be an absolute HTTP or HTTPS URL.");
        }

        return ValidateOptionsResult.Success;
    }
}
