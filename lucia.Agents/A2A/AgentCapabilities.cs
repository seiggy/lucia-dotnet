namespace lucia.Agents.A2A;

public class AgentCapabilities
{
    public bool? Streaming { get; set; }
    public bool? PushNotifications { get; set; }
    public bool? StateTransitionHistory { get; set; }
    public AgentExtension[]? Extensions { get; set; }
}