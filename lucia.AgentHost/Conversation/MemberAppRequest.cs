using lucia.AgentHost.Conversation.Models;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Normalizes a request from a family member's app. The member's identity comes from the
/// authenticated principal: client-supplied user, speaker, and prompt fields are discarded so a
/// member cannot read another person's memories or history, or replace the system prompt.
/// </summary>
public static class MemberAppRequest
{
    public static ConversationRequest ForMember(ConversationRequest request, string memberId) => request with
    {
        PromptOverride = null,
        Context = request.Context with
        {
            // A separate namespace from voice-profile IDs, so the member's app history is their own.
            UserId = $"member:{memberId}",
            SpeakerId = null,
            MemberId = memberId,
            IsMemberApp = true,
            IsDashboardSession = false,
        },
    };
}
