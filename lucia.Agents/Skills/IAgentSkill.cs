using System;
using System.Collections.Generic;
using System.Text;

namespace lucia.Agents.Skills
{
    internal interface IAgentSkill
    {
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }
}
