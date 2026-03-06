using System.Text.Json;

using lucia.Agents.Auth;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Mcp;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.PluginFramework;
using lucia.Agents.Training.Models;
using lucia.Data.Models;
using lucia.TimerAgent.ScheduledTasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace lucia.Data;

/// <summary>
/// EF Core DbContext for all Lucia data entities.
/// Supports both SQLite and PostgreSQL providers.
/// </summary>
public sealed class LuciaDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public LuciaDbContext(DbContextOptions<LuciaDbContext> options) : base(options)
    {
    }

    // ── luciaconfig entities ────────────────────────────────────
    public DbSet<ApiKeyEntry> ApiKeys => Set<ApiKeyEntry>();
    public DbSet<ConfigEntry> ConfigEntries => Set<ConfigEntry>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<McpToolServerDefinition> McpToolServers => Set<McpToolServerDefinition>();
    public DbSet<ModelProvider> ModelProviders => Set<ModelProvider>();
    public DbSet<PresenceSensorMapping> PresenceSensorMappings => Set<PresenceSensorMapping>();
    public DbSet<PluginRepositoryDefinition> PluginRepositories => Set<PluginRepositoryDefinition>();
    public DbSet<InstalledPluginRecord> InstalledPlugins => Set<InstalledPluginRecord>();
    public DbSet<PresenceConfigEntry> PresenceConfig => Set<PresenceConfigEntry>();

    // ── luciatasks entities ─────────────────────────────────────
    public DbSet<ArchivedTask> ArchivedTasks => Set<ArchivedTask>();
    public DbSet<ScheduledTaskDocument> ScheduledTasks => Set<ScheduledTaskDocument>();
    public DbSet<AlarmClock> AlarmClocks => Set<AlarmClock>();
    public DbSet<AlarmSound> AlarmSounds => Set<AlarmSound>();

    // ── luciatraces entities ────────────────────────────────────
    public DbSet<ConversationTrace> ConversationTraces => Set<ConversationTrace>();
    public DbSet<DatasetExportRecord> DatasetExportRecords => Set<DatasetExportRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureApiKeyEntry(modelBuilder);
        ConfigureConfigEntry(modelBuilder);
        ConfigureAgentDefinition(modelBuilder);
        ConfigureMcpToolServerDefinition(modelBuilder);
        ConfigureModelProvider(modelBuilder);
        ConfigurePresenceSensorMapping(modelBuilder);
        ConfigurePluginRepositoryDefinition(modelBuilder);
        ConfigureInstalledPluginRecord(modelBuilder);
        ConfigurePresenceConfigEntry(modelBuilder);
        ConfigureArchivedTask(modelBuilder);
        ConfigureScheduledTaskDocument(modelBuilder);
        ConfigureAlarmClock(modelBuilder);
        ConfigureAlarmSound(modelBuilder);
        ConfigureConversationTrace(modelBuilder);
        ConfigureDatasetExportRecord(modelBuilder);
    }

    private static void ConfigureApiKeyEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiKeyEntry>(entity =>
        {
            entity.ToTable("api_keys");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.HasIndex(e => e.IsRevoked);

            entity.Property(e => e.Scopes)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<string[]>(v, JsonOptions) ?? Array.Empty<string>());
        });
    }

    private static void ConfigureConfigEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConfigEntry>(entity =>
        {
            entity.ToTable("config_entries");
            entity.HasKey(e => e.Key);
            entity.HasIndex(e => e.Section);
        });
    }

    private static void ConfigureAgentDefinition(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentDefinition>(entity =>
        {
            entity.ToTable("agent_definitions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Enabled);

            entity.Property(e => e.Tools)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<AgentToolReference>>(v, JsonOptions) ?? new(),
                    CreateListComparer<AgentToolReference>());
        });
    }

    private static void ConfigureMcpToolServerDefinition(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpToolServerDefinition>(entity =>
        {
            entity.ToTable("mcp_tool_servers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Enabled);

            entity.Property(e => e.EnvironmentVariables)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new(),
                    CreateDictionaryComparer());

            entity.Property(e => e.Headers)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new(),
                    CreateDictionaryComparer());

            entity.Property(e => e.Arguments)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new(),
                    CreateListComparer<string>());
        });
    }

    private static void ConfigureModelProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelProvider>(entity =>
        {
            entity.ToTable("model_providers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Enabled);

            entity.Property(e => e.Auth)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<ModelAuthConfig>(v, JsonOptions) ?? new());

            entity.Property(e => e.CopilotMetadata)
                .HasConversion(
                    v => v != null ? JsonSerializer.Serialize(v, JsonOptions) : null,
                    v => v != null ? JsonSerializer.Deserialize<CopilotModelMetadata>(v, JsonOptions) : null);
        });
    }

    private static void ConfigurePresenceSensorMapping(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PresenceSensorMapping>(entity =>
        {
            entity.ToTable("presence_sensor_mappings");
            entity.HasKey(e => e.EntityId);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.IsUserOverride);
        });
    }

    private static void ConfigurePluginRepositoryDefinition(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PluginRepositoryDefinition>(entity =>
        {
            entity.ToTable("plugin_repositories");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CachedPlugins)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<PluginManifestEntry>>(v, JsonOptions) ?? new(),
                    CreateListComparer<PluginManifestEntry>());
        });
    }

    private static void ConfigureInstalledPluginRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstalledPluginRecord>(entity =>
        {
            entity.ToTable("installed_plugins");
            entity.HasKey(e => e.Id);
        });
    }

    private static void ConfigurePresenceConfigEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PresenceConfigEntry>(entity =>
        {
            entity.ToTable("presence_config");
            entity.HasKey(e => e.Key);
        });
    }

    private static void ConfigureArchivedTask(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArchivedTask>(entity =>
        {
            entity.ToTable("archived_tasks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ArchivedAt);
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.AgentIds)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new(),
                    CreateListComparer<string>());

            entity.Property(e => e.History)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<ArchivedMessage>>(v, JsonOptions) ?? new(),
                    CreateListComparer<ArchivedMessage>());
        });
    }

    private static void ConfigureScheduledTaskDocument(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScheduledTaskDocument>(entity =>
        {
            entity.ToTable("scheduled_tasks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.FireAt);
            entity.HasIndex(e => e.TaskType);
        });
    }

    private static void ConfigureAlarmClock(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AlarmClock>(entity =>
        {
            entity.ToTable("alarm_clocks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IsEnabled);
            entity.HasIndex(e => e.NextFireAt);
        });
    }

    private static void ConfigureAlarmSound(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AlarmSound>(entity =>
        {
            entity.ToTable("alarm_sounds");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IsDefault);
        });
    }

    private static void ConfigureConversationTrace(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationTrace>(entity =>
        {
            entity.ToTable("conversation_traces");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.IsErrored);

            entity.Property(e => e.ConversationHistory)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<TracedMessage>>(v, JsonOptions) ?? new(),
                    CreateListComparer<TracedMessage>());

            entity.Property(e => e.Routing)
                .HasConversion(
                    v => v != null ? JsonSerializer.Serialize(v, JsonOptions) : null,
                    v => v != null ? JsonSerializer.Deserialize<RoutingDecision>(v, JsonOptions) : null);

            entity.Property(e => e.AgentExecutions)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<AgentExecutionRecord>>(v, JsonOptions) ?? new(),
                    CreateListComparer<AgentExecutionRecord>());

            entity.Property(e => e.Label)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<TraceLabel>(v, JsonOptions) ?? new());

            entity.Property(e => e.Metadata)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new(),
                    CreateDictionaryComparer());

            entity.Property(e => e.Spans)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<TracedSpan>>(v, JsonOptions) ?? new(),
                    CreateListComparer<TracedSpan>());
        });
    }

    private static void ConfigureDatasetExportRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DatasetExportRecord>(entity =>
        {
            entity.ToTable("dataset_export_records");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);

            entity.Property(e => e.FilterCriteria)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<ExportFilterCriteria>(v, JsonOptions) ?? new());
        });
    }

    /// <summary>
    /// Creates a value comparer for List&lt;T&gt; properties stored as JSON.
    /// </summary>
    private static ValueComparer<List<T>> CreateListComparer<T>() =>
        new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);

    /// <summary>
    /// Creates a value comparer for Dictionary properties stored as JSON.
    /// </summary>
    private static ValueComparer<Dictionary<string, string>> CreateDictionaryComparer() =>
        new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);
}
