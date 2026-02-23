using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace lucia.Agents.Configuration;

/// <summary>
/// Seeds MongoDB configuration collection from appsettings/environment on first startup.
/// Only seeds when the collection is empty — preserves existing MongoDB config.
/// </summary>
public sealed class ConfigSeeder : IHostedService
{
    private readonly IMongoClient _mongoClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigSeeder> _logger;

    /// <summary>
    /// Only these top-level config sections are seeded to MongoDB.
    /// Everything else (ConnectionStrings, Logging, ASPNETCORE_*, OTEL_*, ports, etc.)
    /// stays in appsettings/env and is never persisted to Mongo.
    /// </summary>
    private static readonly string[] AppConfigSections =
    [
        "HomeAssistant",
        "RouterExecutor",
        "AgentInvoker",
        "ResultAggregator",
        "Redis",
        "MusicAssistant",
        "Agent",
        "PluginDirectory",
        "TraceCapture"
    ];

    private static readonly string[] SensitiveKeywords =
    [
        "token", "password", "secret", "key", "accesskey", "connectionstring"
    ];

    public ConfigSeeder(IMongoClient mongoClient, IConfiguration configuration, ILogger<ConfigSeeder> logger)
    {
        _mongoClient = mongoClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var database = _mongoClient.GetDatabase(ConfigEntry.DatabaseName);
            var collection = database.GetCollection<ConfigEntry>(ConfigEntry.CollectionName);

            var existingCount = await collection.CountDocumentsAsync(
                FilterDefinition<ConfigEntry>.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (existingCount > 0)
            {
                _logger.LogInformation("MongoDB config already seeded ({Count} entries). Skipping.", existingCount);
                return;
            }

            _logger.LogInformation("Seeding MongoDB configuration from appsettings...");

            var entries = new List<ConfigEntry>();
            FlattenConfiguration(_configuration, "", entries);

            if (entries.Count > 0)
            {
                await collection.InsertManyAsync(entries, cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Seeded {Count} configuration entries to MongoDB.", entries.Count);
            }
            else
            {
                _logger.LogWarning("No configuration entries found to seed.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed MongoDB configuration. Application will use appsettings defaults.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void FlattenConfiguration(IConfiguration config, string parentKey, List<ConfigEntry> entries)
    {
        foreach (var child in config.GetChildren())
        {
            var fullKey = string.IsNullOrEmpty(parentKey) ? child.Key : $"{parentKey}:{child.Key}";

            // At the root level, only seed sections in the allowlist
            if (string.IsNullOrEmpty(parentKey) && !IsAppConfigSection(child.Key))
                continue;

            if (child.GetChildren().Any())
            {
                FlattenConfiguration(child, fullKey, entries);
            }
            else
            {
                var value = child.Value;
                if (string.IsNullOrEmpty(value))
                    continue;

                var section = fullKey.Contains(':')
                    ? fullKey[..fullKey.IndexOf(':')]
                    : "Root";

                entries.Add(new ConfigEntry
                {
                    Key = fullKey,
                    Value = value,
                    Section = section,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "system-seed",
                    IsSensitive = IsSensitiveKey(fullKey)
                });
            }
        }
    }

    private static bool IsAppConfigSection(string key)
    {
        return AppConfigSections.Any(s =>
            string.Equals(s, key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSensitiveKey(string key)
    {
        var lowerKey = key.ToLowerInvariant();
        return SensitiveKeywords.Any(keyword => lowerKey.Contains(keyword));
    }
}
