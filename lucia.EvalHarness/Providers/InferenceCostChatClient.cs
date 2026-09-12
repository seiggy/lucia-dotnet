using System.Runtime.CompilerServices;
using lucia.EvalHarness.Configuration;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Providers;

/// <summary>Captures native usage at the raw provider boundary, before tool-call orchestration.</summary>
public sealed class InferenceCostChatClient : DelegatingChatClient
{
    private readonly AsyncLocal<InferenceCostScope?> _scope = new();
    private readonly bool _isLocal;
    private readonly ModelPricing? _pricing;

    public InferenceCostChatClient(IChatClient inner, InferenceBackend backend, string modelName) : base(inner)
    {
        _isLocal = backend.Type is InferenceBackendType.Ollama or InferenceBackendType.OpenAICompat;
        backend.ModelPricing.TryGetValue(modelName, out _pricing);
    }

    public InferenceCostScope BeginScope()
    {
        var scope = new InferenceCostScope(_isLocal, _pricing);
        _scope.Value = scope;
        return scope;
    }

    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(InferenceCostChatClient)
            ? this
            : base.GetService(serviceType, serviceKey);

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var scope = _scope.Value;
        var callIndex = scope?.StartCall() ?? -1;
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        scope?.Record(callIndex, response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scope = _scope.Value;
        var callIndex = scope?.StartCall() ?? -1;
        UsageDetails? usage = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents.OfType<UsageContent>())
            {
                usage ??= new UsageDetails();
                usage.Add(content.Details);
            }

            yield return update;
        }

        // Interrupted streams remain unknown rather than claiming a complete charge from partial usage.
        scope?.Record(callIndex, usage);
    }
}
