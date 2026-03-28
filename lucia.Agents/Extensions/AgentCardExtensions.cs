using A2A;

namespace lucia.Agents.Extensions;

/// <summary>
/// Bridging extensions for A2A 1.0 migration. In A2A 1.0 the agent URL moved
/// from <c>AgentCard.Url</c> to <c>AgentCard.SupportedInterfaces[0].Url</c>.
/// These helpers preserve the codebase's URL access pattern.
/// </summary>
public static class AgentCardExtensions
{
    public static string? GetUrl(this AgentCard card)
        => card.SupportedInterfaces?.FirstOrDefault()?.Url;

    public static void SetUrl(this AgentCard card, string url)
    {
        card.SupportedInterfaces ??= [];
        if (card.SupportedInterfaces.Count == 0)
            card.SupportedInterfaces.Add(new AgentInterface { Url = url });
        else
            card.SupportedInterfaces[0].Url = url;
    }
}
