using System.Runtime.CompilerServices;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Family;

/// <summary>
/// Applies a family member's model controls to every model call made for them: routes the
/// call to their preferred or allowed provider (never silently to a disallowed one) and
/// records token use and estimated cost. Calls outside a <see cref="MemberActionScope"/>
/// pass through unchanged.
/// </summary>
public sealed partial class MemberModelChatClient(
    IChatClient innerClient,
    ModelProvider provider,
    Func<string, CancellationToken, Task<IChatClient?>> resolveProvider,
    ILlmUsageStore? usage,
    TimeProvider timeProvider,
    ILogger<MemberModelChatClient> logger) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (MemberActionScope.Current is not { } scope)
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        if (await RerouteAsync(scope.Member, cancellationToken).ConfigureAwait(false) is { } target)
        {
            return await target.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        await RecordAsync(scope.Member, response.Usage).ConfigureAwait(false);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (MemberActionScope.Current is not { } scope)
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
            yield break;
        }

        if (await RerouteAsync(scope.Member, cancellationToken).ConfigureAwait(false) is { } target)
        {
            await foreach (var update in target.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
            yield break;
        }

        UsageDetails? usageDetails = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents.OfType<UsageContent>())
            {
                usageDetails ??= new UsageDetails();
                usageDetails.Add(content.Details);
            }
            yield return update;
        }

        await RecordAsync(scope.Member, usageDetails).ConfigureAwait(false);
    }

    private async Task<IChatClient?> RerouteAsync(FamilyMember member, CancellationToken cancellationToken)
    {
        var controls = member.LlmControls;
        var targetId = MemberModelPolicy.SelectProvider(controls, provider.Id);
        if (targetId is null)
        {
            return null;
        }

        return await resolveProvider(targetId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Model provider '{provider.Id}' is not allowed for {member.DisplayName}, and the allowed provider '{targetId}' is unavailable.");
    }

    private async Task RecordAsync(FamilyMember member, UsageDetails? details)
    {
        if (usage is null)
        {
            return;
        }

        var input = details?.InputTokenCount ?? 0;
        var output = details?.OutputTokenCount ?? 0;
        var cost = (input * (provider.InputCostPerMillionTokensUsd ?? 0m)
            + output * (provider.OutputCostPerMillionTokensUsd ?? 0m)) / 1_000_000m;
        try
        {
            await usage.RecordAsync(new LlmUsageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                MemberId = member.Id,
                Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                ProviderId = provider.Id,
                Model = provider.ModelName,
                InputTokens = input,
                OutputTokens = output,
                CostUsd = cost,
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The answer was already produced; losing one usage row must not fail the member's request.
            LogUsageRecordFailed(logger, exception, member.Id, provider.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to record model usage for member {MemberId} on provider {ProviderId}")]
    private static partial void LogUsageRecordFailed(ILogger logger, Exception exception, string memberId, string providerId);
}
