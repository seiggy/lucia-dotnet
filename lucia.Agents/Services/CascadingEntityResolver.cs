using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Services;

public sealed class CascadingEntityResolver : ICascadingEntityResolver
{
    private const double PhoneticThreshold = 0.85;
    private const double TokenThreshold = 0.9;

    private static readonly IReadOnlyDictionary<string, string[]> DeviceTypeToDomains =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["lights"] = ["light", "switch"],
            ["light"] = ["light", "switch"],
            ["lamps"] = ["light", "switch"],
            ["lamp"] = ["light", "switch"],
            ["switches"] = ["switch", "light"],
            ["switch"] = ["switch", "light"],
            ["fan"] = ["fan"],
            ["fans"] = ["fan"],
            ["thermostat"] = ["climate"],
            ["climate"] = ["climate"],
            ["ac"] = ["climate"],
            ["heater"] = ["climate"],
            ["music"] = ["media_player"],
            ["speaker"] = ["media_player"],
            ["speakers"] = ["media_player"],
            ["media"] = ["media_player"],
            ["scene"] = ["scene"],
            ["scenes"] = ["scene"],
        };

    private readonly IEntityLocationService _entityLocationService;

    public CascadingEntityResolver(IEntityLocationService entityLocationService)
    {
        _entityLocationService = entityLocationService;
    }

    public CascadeResult Resolve(
        string userQuery,
        string? callerArea,
        string? speakerId,
        IReadOnlyList<string> domains,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_entityLocationService.IsCacheReady)
        {
            return Bail(
                BailReason.CacheNotReady,
                "Entity location cache not loaded; deferring to orchestrator");
        }

        if (string.IsNullOrWhiteSpace(userQuery))
        {
            return Bail(
                BailReason.UnsupportedIntent,
                "Empty user query; cannot resolve entity");
        }

        var intent = QueryDecomposer.Decompose(userQuery, speakerId);
        if (intent.IsComplex)
        {
            return Bail(
                BailReason.ComplexCommand,
                $"Complex command detected ({intent.ComplexityReason}); deferring to orchestrator");
        }

        if (intent.Action is null)
        {
            return Bail(
                BailReason.UnsupportedIntent,
                "No supported action detected; deferring to orchestrator");
        }

        var snapshot = _entityLocationService.GetSnapshot();
        if (snapshot.Entities.IsEmpty)
        {
            return Bail(
                BailReason.CacheNotReady,
                "Entity location cache is empty; deferring to orchestrator");
        }

        var grounded = GroundLocation(intent, callerArea, speakerId, snapshot);
        if (intent.ExplicitLocation is not null && grounded is null)
        {
            return Bail(
                BailReason.NoMatch,
                $"Explicit area '{intent.ExplicitLocation}' not found in cache");
        }

        var candidates = FilterByDomain(intent, grounded, domains, snapshot);
        if (candidates.Count == 0)
        {
            var locationLabel = grounded?.DisplayName ?? "(no area)";
            return Bail(
                BailReason.NoMatch,
                $"No entities found for domains in {locationLabel}");
        }

        var candidateNames = intent.CandidateEntityNames;
        if (IsAreaOnlyCommand(intent, candidateNames, grounded))
        {
            return ResolveFromCandidates(candidates, grounded);
        }

        if (candidateNames.Count == 0)
        {
            return Bail(
                BailReason.Ambiguous,
                "No entity name provided and no area context; deferring to orchestrator");
        }

        var matches = MatchEntities(candidateNames, candidates, snapshot);
        if (matches.Count == 0)
        {
            return Bail(
                BailReason.NoMatch,
                "No entities matched query after cascading filters");
        }

        if (grounded is null && IsAmbiguousAcrossAreas(matches))
        {
            return Bail(
                BailReason.Ambiguous,
                "Multiple matching entities found across different areas");
        }

        return ResolveFromCandidates(matches, grounded);
    }

    private static bool IsAreaOnlyCommand(QueryIntent intent, IReadOnlyList<string> candidateNames, GroundedLocation? grounded)
    {
        if (grounded is null)
            return false;

        if (candidateNames.Count == 0)
            return true;

        return candidateNames.Count == 1
            && intent.DeviceType is not null
            && string.Equals(candidateNames[0], intent.DeviceType, StringComparison.OrdinalIgnoreCase);
    }

    private static CascadeResult ResolveFromCandidates(
        IReadOnlyList<HomeAssistantEntity> entities,
        GroundedLocation? grounded)
    {
        var resolvedIds = entities
            .Select(e => e.EntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CascadeResult
        {
            IsResolved = true,
            ResolvedArea = grounded?.SingleArea?.Name,
            ResolvedFloor = grounded?.Floor?.Name,
            ResolvedEntityIds = resolvedIds
        };
    }

    private GroundedLocation? GroundLocation(QueryIntent intent, string? callerArea, string? speakerId, LocationSnapshot snapshot)
    {
        // Stage 1: Try each candidate area name for exact name/alias match
        foreach (var candidate in intent.CandidateAreaNames)
        {
            var area = _entityLocationService.ExactMatchArea(candidate);
            area ??= MatchAlias(candidate, snapshot);
            if (area is not null)
                return new GroundedLocation([area]);
        }

        // Stage 2: Speaker-disambiguated contains match
        // If "office" didn't match exactly, check if any area names contain the
        // location word. If multiple match (Zack's Office, Dianna's Office), use
        // the speaker ID to disambiguate. If no speaker ID or still ambiguous, bail.
        if (intent.ExplicitLocation is not null)
        {
            var location = intent.ExplicitLocation;
            var containsMatches = new List<AreaInfo>();
            foreach (var area in snapshot.Areas)
            {
                if (area.Name.Contains(location, StringComparison.OrdinalIgnoreCase))
                    containsMatches.Add(area);
            }

            if (containsMatches.Count == 1)
                return new GroundedLocation([containsMatches[0]]);

            if (containsMatches.Count > 1 && !string.IsNullOrWhiteSpace(speakerId))
            {
                var speakerMatch = containsMatches
                    .FirstOrDefault(a => a.Name.Contains(speakerId, StringComparison.OrdinalIgnoreCase));
                if (speakerMatch is not null)
                    return new GroundedLocation([speakerMatch]);
            }

            // Multiple matches + no speaker disambiguation → fall through to floor match
        }

        // Stage 2.5: Floor name/alias match
        // If no area matched, try matching against floor names and aliases.
        // A floor match returns ALL areas on that floor.
        foreach (var candidate in intent.CandidateAreaNames)
        {
            foreach (var floor in snapshot.Floors)
            {
                if (string.Equals(floor.Name, candidate, StringComparison.OrdinalIgnoreCase)
                    || floor.Aliases.Any(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    var floorAreas = snapshot.Areas
                        .Where(a => string.Equals(a.FloorId, floor.FloorId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (floorAreas.Count > 0)
                        return new GroundedLocation(floorAreas, floor);
                }
            }
        }

        // Stage 3: Only fall back to callerArea when the user did NOT name a location.
        // If they said "office lights" and nothing matched, that's ambiguous — bail
        // to the LLM for clarification rather than silently using the device's room.
        if (intent.ExplicitLocation is null && !string.IsNullOrWhiteSpace(callerArea))
        {
            var area = _entityLocationService.ExactMatchArea(callerArea);
            area ??= MatchAlias(callerArea, snapshot);
            if (area is not null)
                return new GroundedLocation([area]);
        }

        return null;
    }

    private static AreaInfo? MatchAlias(string candidate, LocationSnapshot snapshot)
    {
        foreach (var area in snapshot.Areas)
        {
            if (area.Aliases.Any(alias =>
                    string.Equals(alias, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return area;
            }
        }

        return null;
    }

    private static IReadOnlyList<HomeAssistantEntity> FilterByDomain(
        QueryIntent intent,
        GroundedLocation? grounded,
        IReadOnlyList<string> domainFilter,
        LocationSnapshot snapshot)
    {
        IEnumerable<HomeAssistantEntity> candidates;
        if (grounded is not null)
        {
            var areaIds = new HashSet<string>(
                grounded.Areas.Select(a => a.AreaId),
                StringComparer.OrdinalIgnoreCase);
            candidates = snapshot.Entities.Where(e =>
                e.AreaId is not null && areaIds.Contains(e.AreaId));
        }
        else
        {
            candidates = snapshot.Entities;
        }

        var filteredDomains = BuildDomainFilter(domainFilter, intent.DeviceType);
        if (filteredDomains.Count > 0)
        {
            candidates = candidates.Where(e => filteredDomains.Contains(e.Domain));
        }

        candidates = candidates.Where(e => e.IncludeForAgent is null || e.IncludeForAgent.Count > 0);

        return candidates.ToList();
    }

    private static HashSet<string> BuildDomainFilter(IReadOnlyList<string> domainFilter, string? deviceType)
    {
        var filteredDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domainFilter)
        {
            if (!string.IsNullOrWhiteSpace(domain))
                filteredDomains.Add(domain);
        }

        if (deviceType is not null && DeviceTypeToDomains.TryGetValue(deviceType, out var mappedDomains))
        {
            if (filteredDomains.Count == 0)
            {
                filteredDomains.UnionWith(mappedDomains);
            }
            else
            {
                filteredDomains.IntersectWith(mappedDomains);
            }
        }

        return filteredDomains;
    }

    private static IReadOnlyList<HomeAssistantEntity> MatchEntities(
        IReadOnlyList<string> candidateNames,
        IReadOnlyList<HomeAssistantEntity> candidates,
        LocationSnapshot snapshot)
    {
        var uniqueCandidates = candidateNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueCandidates.Length == 0)
            return [];

        var candidateById = candidates.ToDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase);
        var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched = new List<HomeAssistantEntity>();

        foreach (var candidate in uniqueCandidates)
        {
            if (snapshot.EntityById.TryGetValue(candidate, out var entity)
                && candidateById.TryGetValue(entity.EntityId, out var cached))
            {
                if (matchedIds.Add(cached.EntityId))
                    matched.Add(cached);
            }
        }

        if (matched.Count > 0)
            return matched;

        foreach (var entity in candidates)
        {
            if (uniqueCandidates.Any(name =>
                    string.Equals(entity.FriendlyName, name, StringComparison.OrdinalIgnoreCase)
                    || entity.Aliases.Any(alias =>
                        string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))))
            {
                if (matchedIds.Add(entity.EntityId))
                    matched.Add(entity);
            }
        }

        if (matched.Count > 0)
            return matched;

        var phoneticCandidates = uniqueCandidates
            .Select(name => (Name: name, Keys: StringSimilarity.BuildPhoneticKeys(name)))
            .ToArray();

        foreach (var entity in candidates)
        {
            var best = phoneticCandidates.Max(candidate =>
            {
                var score = StringSimilarity.PhoneticSimilarity(candidate.Keys, entity.PhoneticKeys);
                if (entity.AliasPhoneticKeys.Count > 0)
                {
                    var aliasScore = entity.AliasPhoneticKeys.Max(aliasKeys =>
                        StringSimilarity.PhoneticSimilarity(candidate.Keys, aliasKeys));
                    score = Math.Max(score, aliasScore);
                }
                return score;
            });

            if (best >= PhoneticThreshold && matchedIds.Add(entity.EntityId))
                matched.Add(entity);
        }

        if (matched.Count > 0)
            return matched;

        foreach (var entity in candidates)
        {
            var best = uniqueCandidates.Max(name =>
            {
                var score = StringSimilarity.TokenCoreSimilarity(name, entity.FriendlyName);
                if (entity.Aliases.Count > 0)
                {
                    var aliasScore = entity.Aliases.Max(alias =>
                        StringSimilarity.TokenCoreSimilarity(name, alias));
                    score = Math.Max(score, aliasScore);
                }
                return score;
            });

            if (best >= TokenThreshold && matchedIds.Add(entity.EntityId))
                matched.Add(entity);
        }

        return matched;
    }

    private static bool IsAmbiguousAcrossAreas(IReadOnlyList<HomeAssistantEntity> matches)
    {
        var areaIds = new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in matches)
            areaIds.Add(entity.AreaId);

        return areaIds.Count > 1;
    }

    private static CascadeResult Bail(BailReason reason, string explanation) => new()
    {
        IsResolved = false,
        BailReason = reason,
        Explanation = explanation,
        ResolvedEntityIds = []
    };
}
