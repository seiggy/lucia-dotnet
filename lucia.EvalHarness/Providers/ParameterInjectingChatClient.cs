using lucia.EvalHarness.Configuration;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Providers;

/// <summary>
/// A delegating <see cref="IChatClient"/> that injects determinism inference parameters
/// from a <see cref="ModelParameterProfile"/> into every request's <see cref="ChatOptions"/>.
/// </summary>
/// <remarks>
/// Determinism knobs are mapped to the strongly-typed M.E.AI fields whenever an equivalent
/// exists, so that both the OllamaSharp and OpenAI <see cref="IChatClient"/> adapters forward
/// them on the wire:
/// <list type="bullet">
///   <item><description><c>Seed</c> → typed <see cref="ChatOptions.Seed"/> (Ollama <c>seed</c>, OpenAI <c>seed</c>).</description></item>
///   <item><description><c>NumPredict &gt; 0</c> → typed <see cref="ChatOptions.MaxOutputTokens"/> (Ollama <c>num_predict</c>, OpenAI <c>max_completion_tokens</c>).</description></item>
///   <item><description><c>NumPredict == 0</c> → omitted (backend default applies).</description></item>
///   <item><description><c>NumPredict == -1</c> (unlimited) → provider-specific <c>num_predict=-1</c> additional
///     property, which OllamaSharp maps to unlimited generation while the OpenAI adapter ignores it
///     (it never copies additional properties onto the wire). Only added when the caller supplied
///     neither a typed <see cref="ChatOptions.MaxOutputTokens"/> nor a <c>num_predict</c> key, so a
///     caller-provided token limit is never overridden.</description></item>
///   <item><description><c>RepeatPenalty</c> → provider-specific <c>repeat_penalty</c> additional property
///     (Ollama-only; <see cref="ChatOptions.FrequencyPenalty"/> is intentionally not used because it is a
///     different penalty with different semantics).</description></item>
/// </list>
/// In every case the caller's own values win: typed fields are only filled when <see langword="null"/>
/// and provider-specific keys are only added when absent. Unrelated options are preserved.
/// </remarks>
public sealed class ParameterInjectingChatClient : DelegatingChatClient
{
    private const string NumPredictKey = "num_predict";
    private const string RepeatPenaltyKey = "repeat_penalty";

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

        // Seed: strongly typed so both Ollama and OpenAI adapters wire it. Caller wins.
        options.Seed ??= _profile.Seed;

        // Positive NumPredict is a real token cap → strongly typed MaxOutputTokens.
        // Ollama maps it to num_predict; OpenAI maps it to max_completion_tokens. Caller wins.
        if (_profile.NumPredict > 0)
            options.MaxOutputTokens ??= _profile.NumPredict;

        options.AdditionalProperties ??= [];

        // RepeatPenalty has no strongly-typed M.E.AI equivalent (FrequencyPenalty is a different
        // knob), so it is provider-specific. OllamaSharp reads the "repeat_penalty" key; the OpenAI
        // adapter never copies additional properties onto the wire, so it is silently ignored there.
        if (!options.AdditionalProperties.ContainsKey(RepeatPenaltyKey))
            options.AdditionalProperties[RepeatPenaltyKey] = (float)_profile.RepeatPenalty;

        // NumPredict == -1 means "unlimited" for Ollama, which cannot be expressed via the typed
        // MaxOutputTokens (a non-negative token count). Emit it as the provider-specific num_predict
        // key so OllamaSharp preserves unlimited generation, but only when the caller supplied neither
        // a typed token limit nor their own num_predict — otherwise we would override the caller's cap.
        // The OpenAI adapter ignores this key, so nothing negative leaks onto an OpenAI request.
        // NumPredict == 0 is intentionally omitted entirely (the backend default applies).
        if (_profile.NumPredict == -1
            && options.MaxOutputTokens is null
            && !options.AdditionalProperties.ContainsKey(NumPredictKey))
        {
            options.AdditionalProperties[NumPredictKey] = -1;
        }

        return options;
    }
}
