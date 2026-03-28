using lucia.EvalHarness.Configuration;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Providers;

/// <summary>
/// A delegating <see cref="IChatClient"/> that injects Ollama inference parameters
/// from a <see cref="ModelParameterProfile"/> into every request's <see cref="ChatOptions"/>.
/// </summary>
public sealed class ParameterInjectingChatClient : DelegatingChatClient
{
    private readonly ModelParameterProfile _profile;

    public ParameterInjectingChatClient(IChatClient innerClient, ModelParameterProfile profile)
        : base(innerClient)
    {
        _profile = profile;
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = ApplyParameters(options);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = ApplyParameters(options);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    private ChatOptions ApplyParameters(ChatOptions? options)
    {
        options ??= new ChatOptions();
        options.Temperature ??= (float)_profile.Temperature;
        options.TopK ??= _profile.TopK;
        options.TopP ??= (float)_profile.TopP;

        options.AdditionalProperties ??= [];

        if (!options.AdditionalProperties.ContainsKey("num_predict"))
            options.AdditionalProperties["num_predict"] = _profile.NumPredict;

        if (!options.AdditionalProperties.ContainsKey("repeat_penalty"))
            options.AdditionalProperties["repeat_penalty"] = _profile.RepeatPenalty;

        if (_profile.Seed.HasValue && !options.AdditionalProperties.ContainsKey("seed"))
            options.AdditionalProperties["seed"] = _profile.Seed.Value;

        return options;
    }
}
