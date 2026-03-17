using lucia.AgentHost.Extensions;
using lucia.AgentHost.Models;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for managing platform configuration via MongoDB.
/// </summary>
public static class ConfigurationApi
{
    public static IEndpointRouteBuilder MapConfigurationApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/config")
            .WithTags("Configuration")
            .RequireAuthorization();

        group.MapGet("/sections", ListSectionsAsync)
            .WithSummary("List configuration sections")
            .WithDescription("Returns all configuration sections with key counts and last update timestamps. Skill config sections are excluded (managed via Agent Definitions).");
        group.MapGet("/sections/{section}", GetSectionAsync)
            .WithSummary("Get configuration entries for a section")
            .WithDescription("Returns key-value pairs for the specified section. Sensitive values are masked unless showSecrets=true.");
        group.MapPut("/sections/{section}", UpdateSectionAsync)
            .WithSummary("Update configuration entries for a section")
            .WithDescription("Writes values to the MongoDB config collection. Changes hot-reload via the polling loop.");
        group.MapPost("/reset", ResetConfigAsync)
            .WithSummary("Reset configuration to defaults")
            .WithDescription("Re-seeds configuration from appsettings.json, overwriting any MongoDB-stored values.");
        group.MapGet("/schema", GetSchemaAsync)
            .WithSummary("Get configuration schema")
            .WithDescription("Returns JSON schema for all configurable sections including property types and default values.");
        group.MapPost("/test/music-assistant", TestMusicAssistantAsync)
            .WithSummary("Test Music Assistant integration")
            .WithDescription("Sends a test request to the configured Music Assistant instance to verify connectivity.");

        return endpoints;
    }

    /// <summary>
    /// Skill config sections managed via the Agent Definitions page.
    /// Hidden from the raw Configuration page to avoid duplicate editing.
    /// </summary>
    private static readonly HashSet<string> SkillConfigSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "LightControlSkill",
        "ClimateControlSkill",
        "FanControlSkill",
        "MusicPlaybackSkill",
        "SceneControlSkill"
    };

    /// <summary>
    /// Lists all configuration sections with key counts.
    /// Merges MongoDB-stored sections with live IConfiguration sections from the schema.
    /// </summary>
    private static async Task<Ok<List<ConfigSectionSummary>>> ListSectionsAsync(
        IMongoClient mongoClient,
        IConfiguration configuration)
    {
        var collection = GetCollection(mongoClient);
        var entries = await collection.Find(FilterDefinition<ConfigEntry>.Empty).ToListAsync().ConfigureAwait(false);

        var sections = entries
            .GroupBy(e => e.Section)
            .Where(g => !SkillConfigSections.Contains(g.Key))
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
    /// Sensitive values are masked unless <c>showSecrets=true</c> is passed.
    /// </summary>
    private static async Task<Results<Ok<List<ConfigEntryDto>>, NotFound>> GetSectionAsync(
        string section,
        IMongoClient mongoClient,
        IConfiguration configuration,
        [FromQuery] bool showSecrets = false)
    {
        var collection = GetCollection(mongoClient);
        var filter = Builders<ConfigEntry>.Filter.Eq(e => e.Section, section);
        var entries = await collection.Find(filter).ToListAsync().ConfigureAwait(false);

        List<ConfigEntryDto> dtos;

        if (entries.Count > 0)
        {
            dtos = entries.Select(e => new ConfigEntryDto
            {
                Key = e.Key,
                Value = e.Value,
                IsSensitive = e.IsSensitive,
                UpdatedAt = e.UpdatedAt,
                UpdatedBy = e.UpdatedBy
            }).ToList();
        }
        else
        {
            // Fall back to live IConfiguration for sections not stored in MongoDB
            var configSection = configuration.GetSection(section);
            var children = configSection.GetChildren().ToList();

            if (children.Count == 0)
            {
                return TypedResults.NotFound();
            }

            dtos = new List<ConfigEntryDto>();
            FlattenConfigSection(configSection, section, section, dtos);
        }

        if (!showSecrets)
        {
            foreach (var dto in dtos)
            {
                if (dto.IsSensitive && dto.Value is not null)
                {
                    dto.Value = "********";
                }
            }
        }

        return TypedResults.Ok(dtos);
    }

    /// <summary>
    /// Recursively flattens an IConfigurationSection into ConfigEntryDto list.
    /// </summary>
    private static void FlattenConfigSection(
        IConfigurationSection configSection, string rootSection, string parentKey,
        List<ConfigEntryDto> results)
    {
        foreach (var child in configSection.GetChildren())
        {
            var fullKey = $"{parentKey}:{child.Key}";

            if (child.GetChildren().Any() && child.Value is null)
            {
                FlattenConfigSection(child, rootSection, fullKey, results);
            }
            else
            {
                var isSensitive = IsSensitiveKey(fullKey);
                results.Add(new ConfigEntryDto
                {
                    Key = fullKey,
                    Value = child.Value,
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

            await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }).ConfigureAwait(false);
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
        var result = await collection.DeleteManyAsync(FilterDefinition<ConfigEntry>.Empty).ConfigureAwait(false);
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

    /// <summary>
    /// Tests the Music Assistant integration by querying the library with the provided IntegrationId.
    /// </summary>
    private static async Task<Results<Ok<MusicAssistantTestResult>, BadRequest<string>>> TestMusicAssistantAsync(
        [FromBody] MusicAssistantTestRequest request,
        [FromServices] IHomeAssistantClient haClient,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IntegrationId))
        {
            return TypedResults.BadRequest("IntegrationId is required.");
        }

        try
        {
            var serviceRequest = new ServiceCallRequest
            {
                ["media_type"] = "track",
                ["limit"] = 1,
                ["order_by"] = "random",
                ["config_entry_id"] = request.IntegrationId
            };

            var response = await haClient.CallServiceAsync<lucia.Agents.Skills.Models.MusicLibraryResponse>(
                "music_assistant", "get_library", "return_response=1", serviceRequest, cancellationToken).ConfigureAwait(false);

            var trackCount = response.ServiceResponse.Items.Count;
            return TypedResults.Ok(new MusicAssistantTestResult
            {
                Success = true,
                Message = trackCount > 0
                    ? $"Connection successful — Music Assistant library returned {trackCount} track(s)."
                    : "Connection successful but the library appears empty."
            });
        }
        catch (Exception ex)
        {
            return TypedResults.Ok(new MusicAssistantTestResult
            {
                Success = false,
                Message = $"Integration test failed: {ex.Message}"
            });
        }
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
                new("SemanticSimilarityThreshold", "number", "Minimum cosine similarity for routing cache semantic hits (0.0-1.0)", "0.95"),
                new("ChatCacheSemanticThreshold", "number", "Minimum cosine similarity for chat cache semantic hits (0.0-1.0)", "0.98"),
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
                new("Timeout", "string", "Agent execution timeout (HH:MM:SS)", "00:00:30")
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
            Section = "LightControlSkill",
            Description = "Light control entity matching parameters (tuned via Skill Optimizer)",
            Properties =
            [
                new("HybridSimilarityThreshold", "number", "Minimum hybrid score for match inclusion (0.0-1.0)", "0.55"),
                new("EmbeddingWeight", "number", "Weight for embedding vs string similarity (0.0-1.0)", "0.4"),
                new("ScoreDropoffRatio", "number", "Min fraction of top score to keep (0.0-1.0, 0=disabled)", "0.80"),
                new("DisagreementPenalty", "number", "Penalty when string metrics disagree (0.0-1.0, 0=disabled)", "0.4"),
                new("EmbeddingResolutionMargin", "number", "Margin within which embedding ties are resolved by string scores (0.0-1.0)", "0.30"),
                new("CacheRefreshMinutes", "number", "Entity cache refresh interval in minutes", "30")
            ]
        },
        new()
        {
            Section = "ClimateControlSkill",
            Description = "Climate control entity matching parameters (tuned via Skill Optimizer)",
            Properties =
            [
                new("HybridSimilarityThreshold", "number", "Minimum hybrid score for match inclusion (0.0-1.0)", "0.55"),
                new("EmbeddingWeight", "number", "Weight for embedding vs string similarity (0.0-1.0)", "0.4"),
                new("ScoreDropoffRatio", "number", "Min fraction of top score to keep (0.0-1.0, 0=disabled)", "0.80"),
                new("DisagreementPenalty", "number", "Penalty when string metrics disagree (0.0-1.0, 0=disabled)", "0.4"),
                new("EmbeddingResolutionMargin", "number", "Margin within which embedding ties are resolved by string scores (0.0-1.0)", "0.30"),
                new("CacheRefreshMinutes", "number", "Entity cache refresh interval in minutes", "30")
            ]
        },
        new()
        {
            Section = "FanControlSkill",
            Description = "Fan control entity matching parameters (tuned via Skill Optimizer)",
            Properties =
            [
                new("HybridSimilarityThreshold", "number", "Minimum hybrid score for match inclusion (0.0-1.0)", "0.55"),
                new("EmbeddingWeight", "number", "Weight for embedding vs string similarity (0.0-1.0)", "0.4"),
                new("ScoreDropoffRatio", "number", "Min fraction of top score to keep (0.0-1.0, 0=disabled)", "0.80"),
                new("DisagreementPenalty", "number", "Penalty when string metrics disagree (0.0-1.0, 0=disabled)", "0.4"),
                new("EmbeddingResolutionMargin", "number", "Margin within which embedding ties are resolved by string scores (0.0-1.0)", "0.30"),
                new("CacheRefreshMinutes", "number", "Entity cache refresh interval in minutes", "5")
            ]
        },
        new()
        {
            Section = "Redis",
            Description = "Redis connection and task persistence settings",
            Properties =
            [
                new("TaskTtlHours", "number", "Task TTL in hours", "24"),
                new("PromptCacheTtlHours", "number", "Prompt cache entry TTL in hours (LRU — refreshed on hit)", "48"),
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
                new("IntegrationId", "string", "Home Assistant integration entry ID for Music Assistant. " +
                                               "To find this: open your HA config directory, look in .storage/core.config_entries, " +
                                               "search for \"music_assistant\", and copy the entry_id value.", "")
            ]
        },
        new()
        {
            Section = "TraceCapture",
            Description = "Conversation trace capture and retention settings",
            Properties =
            [
                new("Enabled", "boolean", "Whether trace capture is enabled", "true"),
                new("RetentionDays", "number", "Number of days to retain unlabeled traces before cleanup", "30"),
                new("RedactionPatterns", "array", "Regex patterns for redacting sensitive data from trace content", ""),
                new("DatabaseName", "string", "MongoDB database for traces", "luciatraces"),
                new("TracesCollectionName", "string", "Collection name for traces", "traces"),
                new("ExportsCollectionName", "string", "Collection name for exports", "exports")
            ]
        },
        new()
        {
            Section = "PersonalityPrompt",
            Description = "Customize how Lucia responds with a personality prompt",
            Properties =
            [
                new("Instructions", "textarea",
                    "System prompt that defines Lucia's personality and communication style. " +
                    "When set, agent responses are rewritten using this prompt before being returned to the user. " +
                    "Leave empty to return raw agent responses.", ""),
                new("ModelConnectionName", "model-select",
                    "Model provider name for personality rewriting. " +
                    "Leave empty to use the orchestrator's default model. " +
                    "Set to a configured model provider name to use a different LLM for personality rewriting.", "")
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
            Section = "Wyoming:HuggingFace",
            Description = "Hugging Face Hub integration for downloading ONNX models from the onnx-community organization",
            Properties =
            [
                new("ApiToken", "string",
                    "Hugging Face API token (User Access Token). " +
                    "When set, enables browsing and downloading ONNX speech models from Hugging Face. " +
                    "Generate a token at https://huggingface.co/settings/tokens", "", true)
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