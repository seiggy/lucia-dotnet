namespace lucia.Agents.Configuration;

using A2A;
using Microsoft.Agents.AI.Hosting;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Extracts domain capabilities from agent descriptions and builds domain-to-agent mappings.
/// Agents declare their domain responsibility using #hashtags in their descriptions.
/// Example: "Controls lighting for the home #lighting #brightness #scenes"
/// </summary>
public static class DomainCapabilitiesExtractor
{
    /// <summary>
    /// Builds a domain-to-agent mapping from agent catalog.
    /// Parses hashtags in agent descriptions to extract domain capabilities.
    /// </summary>
    /// <param name="agents">Collection of agent cards from the catalog</param>
    /// <returns>Dictionary mapping domain names to lists of agent IDs that handle them</returns>
    public static Dictionary<string, List<string>> BuildDomainAgentMap(IEnumerable<AgentCard> agents)
    {
        var domainMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agents)
        {
            var domains = ExtractDomainsFromDescription(agent.Description);
            
            foreach (var domain in domains)
            {
                if (!domainMap.ContainsKey(domain))
                {
                    domainMap[domain] = new();
                }
                
                // Use agent name (normalize to lowercase with -agent suffix if not present)
                var agentId = NormalizeAgentId(agent.Name);
                if (!domainMap[domain].Contains(agentId, StringComparer.OrdinalIgnoreCase))
                {
                    domainMap[domain].Add(agentId);
                }
            }
        }

        return domainMap;
    }

    /// <summary>
    /// Extracts domain hashtags from an agent description.
    /// Hashtags are case-insensitive and represent domain capabilities.
    /// Example: "#lighting #brightness #scenes" -> ["lighting", "brightness", "scenes"]
    /// </summary>
    /// <param name="description">Agent description text</param>
    /// <returns>List of extracted domain keywords (lowercase)</returns>
    public static List<string> ExtractDomainsFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return new();

        var domains = new List<string>();
        var hashtagPattern = @"#(\w+)";
        var matches = Regex.Matches(description, hashtagPattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var domain = match.Groups[1].Value.ToLowerInvariant();
            if (!string.IsNullOrEmpty(domain) && !domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    /// <summary>
    /// Finds the best matching domain for a set of keywords using the domain agent map.
    /// Uses keyword frequency and domain keyword overlap to determine best match.
    /// </summary>
    /// <param name="keywords">List of keywords extracted from user message</param>
    /// <param name="domainAgentMap">Domain to agent mapping from agent catalog</param>
    /// <returns>Best matching domain or null if no good match found</returns>
    public static string? FindDomainForKeywords(List<string> keywords, IReadOnlyDictionary<string, List<string>> domainAgentMap)
    {
        if (keywords.Count == 0 || domainAgentMap.Count == 0)
            return null;

        var domainScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Score each domain based on keyword overlap
        foreach (var domain in domainAgentMap.Keys)
        {
            var score = 0;
            var domainWords = domain.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var keyword in keywords)
            {
                // Exact domain word match gets highest score
                if (domainWords.Any(w => w.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 10;
                }
                // Keyword contains domain word or vice versa
                else if (keyword.Contains(domain, StringComparison.OrdinalIgnoreCase) ||
                         domain.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 5;
                }
                // Partial overlap (common substrings)
                else if (HasCommonSubstring(keyword, domain, minLength: 3))
                {
                    score += 2;
                }
            }

            if (score > 0)
            {
                domainScores[domain] = score;
            }
        }

        // Return highest-scoring domain
        if (domainScores.Count > 0)
        {
            var bestDomain = domainScores.MaxBy(kvp => kvp.Value).Key;
            return bestDomain;
        }

        return null;
    }

    /// <summary>
    /// Normalizes an agent ID to consistent format (e.g., "LightAgent" -> "light-agent").
    /// </summary>
    /// <param name="agentId">The agent ID to normalize</param>
    /// <returns>Normalized agent ID in lowercase with hyphens</returns>
    private static string NormalizeAgentId(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return agentId;

        // Convert PascalCase to kebab-case
        var kebab = Regex.Replace(agentId, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
        
        // Ensure -agent suffix
        if (!kebab.EndsWith("-agent", StringComparison.OrdinalIgnoreCase))
        {
            kebab += "-agent";
        }

        return kebab;
    }

    /// <summary>
    /// Checks if two strings have common substrings of at least minLength characters.
    /// </summary>
    /// <param name="str1">First string</param>
    /// <param name="str2">Second string</param>
    /// <param name="minLength">Minimum substring length to consider</param>
    /// <returns>True if common substring found</returns>
    private static bool HasCommonSubstring(string str1, string str2, int minLength = 3)
    {
        var s1 = str1.ToLowerInvariant();
        var s2 = str2.ToLowerInvariant();

        for (int i = 0; i <= s1.Length - minLength; i++)
        {
            var sub = s1.Substring(i, minLength);
            if (s2.Contains(sub))
                return true;
        }

        return false;
    }
}
