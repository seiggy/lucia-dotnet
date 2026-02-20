using lucia.Agents.Configuration;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Minimal API endpoints for managing platform configuration via MongoDB.
/// </summary>
public static class ConfigurationApi
{
    public static IEndpointRouteBuilder MapConfigurationApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/config")
            .WithTags("Configuration");

        group.MapGet("/sections", ListSectionsAsync);
        group.MapGet("/sections/{section}", GetSectionAsync);
        group.MapPut("/sections/{section}", UpdateSectionAsync);
        group.MapPost("/reset", ResetConfigAsync);
        group.MapGet("/schema", GetSchemaAsync);

        return endpoints;
    }

    /// <summary>
    /// Lists all configuration sections with key counts.
    /// Merges MongoDB-stored sections with live IConfiguration sections from the schema.
    /// </summary>
    private static async Task<Ok<List<ConfigSectionSummary>>> ListSectionsAsync(
        IMongoClient mongoClient,
        IConfiguration configuration)
    {
        var collection = GetCollection(mongoClient);
        var entries = await collection.Find(FilterDefinition<ConfigEntry>.Empty).ToListAsync();

        var sections = entries
            .GroupBy(e => e.Section)
            .Select(g => new ConfigSectionSummary
            {
                Section = g.Key,
                KeyCount = g.Count(),
                LastUpdated = g.Max(e => e.UpdatedAt)
            })
            .ToDictionary(s => s.Section);

        // Add sections from live config that aren't in MongoDB (e.g. ConnectionStrings)
        foreach (var schema in GetAllSchemas())
        {
            if (sections.ContainsKey(schema.Section))
                continue;

            var configSection = configuration.GetSection(schema.Section);
            var children = configSection.GetChildren().ToList();
            if (children.Count > 0)
            {
                sections[schema.Section] = new ConfigSectionSummary
                {
                    Section = schema.Section,
                    KeyCount = children.Count,
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        return TypedResults.Ok(sections.Values.OrderBy(s => s.Section).ToList());
    }

    /// <summary>
    /// Gets all key-value pairs for a configuration section.
    /// Falls back to live IConfiguration if no MongoDB entries exist for the section.
    /// Sensitive values are masked unless ?showSecrets=true is passed.
    /// </summary>
    private static async Task<Results<Ok<List<ConfigEntryDto>>, NotFound>> GetSectionAsync(
        string section,
        IMongoClient mongoClient,
        IConfiguration configuration,
        [FromQuery] bool showSecrets = false)
    {
        var collection = GetCollection(mongoClient);
        var filter = Builders<ConfigEntry>.Filter.Eq(e => e.Section, section);
        var entries = await collection.Find(filter).ToListAsync();

        if (entries.Count > 0)
        {
            var dtos = entries.Select(e => new ConfigEntryDto
            {
                Key = e.Key,
                Value = e.IsSensitive && !showSecrets ? "********" : e.Value,
                IsSensitive = e.IsSensitive,
                UpdatedAt = e.UpdatedAt,
                UpdatedBy = e.UpdatedBy
            }).ToList();

            return TypedResults.Ok(dtos);
        }

        // Fall back to live IConfiguration for sections not stored in MongoDB
        var configSection = configuration.GetSection(section);
        var children = configSection.GetChildren().ToList();

        if (children.Count == 0)
        {
            return TypedResults.NotFound();
        }

        var liveDtos = new List<ConfigEntryDto>();
        FlattenConfigSection(configSection, section, section, showSecrets, liveDtos);

        return TypedResults.Ok(liveDtos);
    }

    /// <summary>
    /// Recursively flattens an IConfigurationSection into ConfigEntryDto list.
    /// </summary>
    private static void FlattenConfigSection(
        IConfigurationSection configSection, string rootSection, string parentKey,
        bool showSecrets, List<ConfigEntryDto> results)
    {
        foreach (var child in configSection.GetChildren())
        {
            var fullKey = $"{parentKey}:{child.Key}";

            if (child.GetChildren().Any() && child.Value is null)
            {
                FlattenConfigSection(child, rootSection, fullKey, showSecrets, results);
            }
            else
            {
                var isSensitive = IsSensitiveKey(fullKey);
                results.Add(new ConfigEntryDto
                {
                    Key = fullKey,
                    Value = isSensitive && !showSecrets ? "********" : child.Value,
                    IsSensitive = isSensitive,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "live-config"
                });
            }
        }
    }

    /// <summary>
    /// Updates configuration values for a section.
    /// </summary>
    private static async Task<Results<Ok<int>, BadRequest<string>>> UpdateSectionAsync(
        string section,
        [FromBody] Dictionary<string, string?> values,
        IMongoClient mongoClient)
    {
        if (values is null || values.Count == 0)
        {
            return TypedResults.BadRequest("No values provided.");
        }

        var collection = GetCollection(mongoClient);
        var updateCount = 0;

        foreach (var (key, value) in values)
        {
            var fullKey = key.Contains(':') ? key : $"{section}:{key}";
            var filter = Builders<ConfigEntry>.Filter.Eq(e => e.Key, fullKey);

            var update = Builders<ConfigEntry>.Update
                .Set(e => e.Value, value)
                .Set(e => e.UpdatedAt, DateTime.UtcNow)
                .Set(e => e.UpdatedBy, "admin-ui")
                .SetOnInsert(e => e.Section, section)
                .SetOnInsert(e => e.IsSensitive, IsSensitiveKey(fullKey));

            await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
            updateCount++;
        }

        return TypedResults.Ok(updateCount);
    }

    /// <summary>
    /// Resets MongoDB config by deleting all entries. ConfigSeeder will re-seed on next restart.
    /// </summary>
    private static async Task<Ok<string>> ResetConfigAsync(
        IMongoClient mongoClient)
    {
        var collection = GetCollection(mongoClient);
        var result = await collection.DeleteManyAsync(FilterDefinition<ConfigEntry>.Empty);
        return TypedResults.Ok($"Deleted {result.DeletedCount} configuration entries. Restart services to re-seed from appsettings.");
    }

    /// <summary>
    /// Returns typed schema for all known configuration sections.
    /// Used by the dashboard UI to generate dynamic forms.
    /// </summary>
    private static Ok<List<ConfigSectionSchema>> GetSchemaAsync()
    {
        return TypedResults.Ok(GetAllSchemas());
    }

    private static List<ConfigSectionSchema> GetAllSchemas() =>
    [
        new()
        {
            Section = "HomeAssistant",
            Description = "Home Assistant connection and API settings",
            Properties =
            [
                new("BaseUrl", "string", "Home Assistant base URL", "http://homeassistant.local:8123"),
                new("AccessToken", "string", "Long-lived access token", "", true),
                new("TimeoutSeconds", "number", "API timeout in seconds", "60"),
                new("ValidateSSL", "boolean", "Validate SSL certificates", "false")
            ]
        },
        new()
        {
            Section = "RouterExecutor",
            Description = "Router agent configuration for intent routing",
            Properties =
            [
                new("ConfidenceThreshold", "number", "Minimum confidence to accept routing (0.0-1.0)", "0.7"),
                new("MaxAttempts", "number", "Maximum routing retry attempts", "2"),
                new("Temperature", "number", "LLM temperature for routing (0.0-2.0)", "1.0"),
                new("MaxOutputTokens", "number", "Maximum output tokens for router", "512"),
                new("IncludeAgentCapabilities", "boolean", "Include agent capabilities in router prompt", "true"),
                new("IncludeSkillExamples", "boolean", "Include skill examples in router prompt", "false")
            ]
        },
        new()
        {
            Section = "AgentInvoker",
            Description = "Agent execution timeout settings",
            Properties =
            [
                new("Timeout", "string", "Agent execution timeout (HH:MM:SS)", "00:03:00")
            ]
        },
        new()
        {
            Section = "ResultAggregator",
            Description = "Response aggregation settings",
            Properties =
            [
                new("DefaultFallbackMessage", "string", "Fallback message when processing fails", "I encountered an issue processing your request. Please try again."),
                new("EnableNaturalLanguageJoining", "boolean", "Join multi-agent responses naturally", "true")
            ]
        },
        new()
        {
            Section = "Redis",
            Description = "Redis connection and task persistence settings",
            Properties =
            [
                new("TaskTtlHours", "number", "Task TTL in hours", "24"),
                new("ConnectRetryCount", "number", "Connection retry count", "3"),
                new("ConnectTimeout", "number", "Connection timeout in milliseconds", "5000")
            ]
        },
        new()
        {
            Section = "MusicAssistant",
            Description = "Music Assistant integration settings",
            Properties =
            [
                new("IntegrationId", "string", "Home Assistant integration entry ID for Music Assistant", "")
            ]
        },
        new()
        {
            Section = "TraceCapture",
            Description = "Conversation trace capture and retention settings",
            Properties =
            [
                new("DatabaseName", "string", "MongoDB database for traces", "luciatraces"),
                new("TracesCollectionName", "string", "Collection name for traces", "traces"),
                new("ExportsCollectionName", "string", "Collection name for exports", "exports"),
                new("RetentionDays", "number", "Trace retention period in days", "30")
            ]
        },
        new()
        {
            Section = "ConnectionStrings",
            Description = "Service connection strings (AI models, databases, caches)",
            Properties =
            [
                new("redis", "string", "Redis connection string", "localhost:6379", true),
                new("ollama-phi3-mini", "string", "Ollama Phi-3 mini connection", ""),
                new("ollama-llama3-2-3b", "string", "Ollama LLaMA 3.2 3B connection", "")
            ]
        },
        new()
        {
            Section = "Agents",
            Description = "Agent configuration array — each agent has name, type, skills, and optional model override",
            Properties =
            [
                new("__isArray", "boolean", "This section is a JSON array of agent configurations", "true"),
                new("AgentName", "string", "Agent identifier name", ""),
                new("AgentType", "string", "Fully qualified agent class name", ""),
                new("AgentSkills", "array", "List of skill class names", "[]"),
                new("AgentConfig", "string", "Optional config section name for agent options", ""),
                new("ModelConnectionName", "string", "Optional connection name for per-agent model override", "")
            ]
        }
    ];

    private static IMongoCollection<ConfigEntry> GetCollection(IMongoClient mongoClient)
    {
        var database = mongoClient.GetDatabase(ConfigEntry.DatabaseName);
        return database.GetCollection<ConfigEntry>(ConfigEntry.CollectionName);
    }

    private static bool IsSensitiveKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains("token") || lower.Contains("password") || lower.Contains("secret") ||
               lower.Contains("key") || lower.Contains("accesskey") || lower.Contains("connectionstring");
    }
}

