namespace lucia.Agents.Skills
{
    internal interface IAgentSkill
    {
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }
}
