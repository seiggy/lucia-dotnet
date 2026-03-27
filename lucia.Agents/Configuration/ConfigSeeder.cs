using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Configuration;

/// <summary>
/// Seeds the configuration store from appsettings/environment on first startup.
/// Only seeds when the store is empty — preserves existing config.
/// </summary>
public sealed class ConfigSeeder : IHostedService
{
    private readonly IConfigStoreWriter _configStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigSeeder> _logger;

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
        "TraceCapture",
        "Wyoming",
        "PersonalityPrompt",
        LightControlSkillOptions.SectionName,
        ClimateControlSkillOptions.SectionName,
        FanControlSkillOptions.SectionName,
        "MusicPlaybackSkill",
        SceneControlSkillOptions.SectionName
    ];

    private static readonly string[] SensitiveKeywords =
    [
        "token", "password", "secret", "key", "accesskey", "connectionstring"
    ];

    public ConfigSeeder(IConfigStoreWriter configStore, IConfiguration configuration, ILogger<ConfigSeeder> logger)
    {
        _configStore = configStore;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var existingCount = await _configStore.GetEntryCountAsync(cancellationToken).ConfigureAwait(false);

            var entries = new List<ConfigEntry>();
            FlattenConfiguration(_configuration, "", entries);

            if (existingCount == 0)
            {
                _logger.LogInformation("Seeding configuration store from appsettings...");
                if (entries.Count > 0)
                {
                    await _configStore.InsertManyAsync(entries, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Seeded {Count} configuration entries.", entries.Count);
                }
                return;
            }

            var existingKeys = await _configStore.GetAllKeysAsync(cancellationToken).ConfigureAwait(false);

            var missing = entries.Where(e => !existingKeys.Contains(e.Key)).ToList();
            if (missing.Count > 0)
            {
                await _configStore.InsertManyAsync(missing, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Seeded {Count} new configuration entries for upgraded sections.", missing.Count);
            }
            else
            {
                _logger.LogDebug("Config store up to date ({Count} entries). No new sections to seed.", existingCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed configuration store. Application will use appsettings defaults.");
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
                    ? fullKey[..fullKey.LastIndexOf(':')]
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
