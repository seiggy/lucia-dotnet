using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training.Models;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Composite observer that delegates to all registered <see cref="IOrchestratorObserver"/>
/// implementations. Allows both <see cref="Training.TraceCaptureObserver"/> and
/// <see cref="LiveActivityObserver"/> to receive pipeline events.
/// </summary>
public sealed class CompositeOrchestratorObserver : IOrchestratorObserver
{
    private readonly IOrchestratorObserver[] _observers;

    public CompositeOrchestratorObserver(IEnumerable<IOrchestratorObserver> observers)
    {
        _observers = observers.ToArray();
    }

    public async Task<string> OnRequestStartedAsync(
        string userRequest,
        IReadOnlyList<TracedMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        string requestId = string.Empty;
        foreach (var observer in _observers)
        {
            var id = await observer.OnRequestStartedAsync(userRequest, conversationHistory, cancellationToken)
                .ConfigureAwait(false);

            // Use the first non-empty requestId (from TraceCaptureObserver)
            if (requestId.Length == 0 && id.Length > 0)
                requestId = id;
        }

        return requestId;
    }

    public async Task OnRoutingCompletedAsync(
        string requestId,
        AgentChoiceResult result,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            await observer.OnRoutingCompletedAsync(requestId, result, systemPrompt, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task OnAgentExecutionCompletedAsync(
        string requestId,
        OrchestratorAgentResponse response,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            await observer.OnAgentExecutionCompletedAsync(requestId, response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task OnResponseAggregatedAsync(
        string requestId,
        string aggregatedResponse,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            await observer.OnResponseAggregatedAsync(requestId, aggregatedResponse, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
