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

    public async Task OnRequestStartedAsync(
        string userRequest,
        IReadOnlyList<TracedMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            await observer.OnRequestStartedAsync(userRequest, conversationHistory, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task OnRoutingCompletedAsync(
        AgentChoiceResult result,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            await observer.OnRoutingCompletedAsync(result, systemPrompt, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task OnAgentExecutionCompletedAsync(
        OrchestratorAgentResponse response,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            await observer.OnAgentExecutionCompletedAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task OnResponseAggregatedAsync(
        string aggregatedResponse,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            await observer.OnResponseAggregatedAsync(aggregatedResponse, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
