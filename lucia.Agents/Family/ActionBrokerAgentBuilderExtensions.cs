using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Family;

/// <summary>
/// Puts the <see cref="ActionBroker"/> in front of an agent for requests that run in a
/// <see cref="MemberActionScope"/>. Requests without a scope are unchanged.
/// </summary>
public static class ActionBrokerAgentBuilderExtensions
{
    public static AIAgentBuilder UseLuciaActionBroker(this AIAgentBuilder builder) =>
        builder.Use((inner, _) =>
        {
            var guarded = inner.AsBuilder();
            var canMediateTools = inner.GetService<FunctionInvokingChatClient>() is not null;
            if (canMediateTools)
            {
                guarded = guarded.Use(AuthorizeFunctionAsync);
            }

            return guarded
                .Use(
                    (messages, session, options, agent, ct) => RunGuardedAsync(messages, session, options, agent, canMediateTools, ct),
                    (messages, session, options, agent, ct) => RunStreamingGuardedAsync(messages, session, options, agent, canMediateTools, ct))
                .Build();
        });

    private static async ValueTask<object?> AuthorizeFunctionAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        if (MemberActionScope.Current is not { } scope)
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        var outcome = await scope.Broker
            .AuthorizeToolAsync(scope.Member, agent.Id, context.Function.Name, context.Arguments, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Kind != BrokerOutcomeKind.Proceed)
        {
            return outcome.Message;
        }

        if (outcome.Record is null)
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var result = await next(context, cancellationToken).ConfigureAwait(false);
            await scope.Broker.CompleteAsync(outcome.Record, succeeded: true, result?.ToString(), CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            await scope.Broker.CompleteAsync(outcome.Record, succeeded: false, exception.Message, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static Task<AgentResponse> RunGuardedAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent agent,
        bool canMediateTools,
        CancellationToken cancellationToken) =>
        Refusal(agent, canMediateTools) is { } refusal
            ? Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, refusal)))
            : agent.RunAsync(messages, session, options, cancellationToken);

    private static async IAsyncEnumerable<AgentResponseUpdate> RunStreamingGuardedAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent agent,
        bool canMediateTools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Refusal(agent, canMediateTools) is { } refusal)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, refusal);
            yield break;
        }

        await foreach (var update in agent.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private static string? Refusal(AIAgent agent, bool canMediateTools)
    {
        if (MemberActionScope.Current is not { } scope)
        {
            return null;
        }

        if (scope.Broker.Evaluate(scope.Member, CapabilityKind.Agent, agent.Id) == ActionRule.Deny)
        {
            ActionBrokerTelemetry.Decisions.Add(1, new KeyValuePair<string, object?>("outcome", "AgentDenied"));
            return $"Sorry, {scope.Member.DisplayName} isn't allowed to use that.";
        }

        if (!canMediateTools && ActionBroker.RequiresMediation(scope.Member))
        {
            ActionBrokerTelemetry.Decisions.Add(1, new KeyValuePair<string, object?>("outcome", "Unmediated"));
            return $"Sorry, that isn't available for {scope.Member.DisplayName}.";
        }

        return null;
    }
}
