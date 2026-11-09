using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Family;

/// <summary>
/// Appends a family member's system prompt and blocked-topic guidance to an agent's
/// instructions. Topic and content gates still run at the conversation boundary; this only
/// steers the model.
/// </summary>
public sealed class MemberGuidanceChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, WithGuidance(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, WithGuidance(options), cancellationToken);

    private static ChatOptions? WithGuidance(ChatOptions? options)
    {
        if (MemberActionScope.Current?.Member is not { } member)
        {
            return options;
        }

        var controls = member.LlmControls;
        if (controls.SystemPrompt is null && controls.BlockedTopics.Count == 0)
        {
            return options;
        }

        var guidance = new StringBuilder();
        guidance.Append("You are talking with ").Append(member.DisplayName).Append('.');
        if (controls.SystemPrompt is not null)
        {
            guidance.AppendLine().Append(controls.SystemPrompt);
        }
        if (controls.BlockedTopics.Count > 0)
        {
            guidance.AppendLine()
                .Append("Do not discuss these topics; politely decline instead: ")
                .AppendJoin(", ", controls.BlockedTopics)
                .Append('.');
        }

        var updated = options?.Clone() ?? new ChatOptions();
        updated.Instructions = string.IsNullOrEmpty(updated.Instructions)
            ? guidance.ToString()
            : updated.Instructions + Environment.NewLine + Environment.NewLine + guidance;
        return updated;
    }
}
