using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Initializes plugin tool providers once per host and shares their tools between agents.
/// Provider names are unique, stable keys in saved tool references.
/// </summary>
public sealed partial class PluginToolRegistry : IDisposable
{
    private const string Prefix = "plugin:";
    private readonly IReadOnlyDictionary<string, IAgentToolProvider> _providers;
    private readonly Dictionary<string, IList<AITool>> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly ILogger<PluginToolRegistry> _logger;

    public PluginToolRegistry(IEnumerable<IAgentToolProvider> providers, ILogger<PluginToolRegistry> logger)
    {
        _providers = providers.ToDictionary(provider =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.Name);
            return Prefix + provider.Name;
        }, StringComparer.Ordinal);
        _logger = logger;
    }

    public static bool IsPluginReference(AgentToolReference reference) =>
        string.Equals(reference.Source, "plugin", StringComparison.Ordinal);

    public async Task<IReadOnlyList<PluginToolProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        return _providers.Select(entry => new PluginToolProviderInfo
        {
            ServerId = entry.Key,
            Name = entry.Value.Name,
            Description = entry.Value.Description,
            ErrorMessage = _errors.GetValueOrDefault(entry.Key),
            Tools = _tools.TryGetValue(entry.Key, out var tools)
                ? tools.Select(tool => new McpToolInfo
                {
                    ServerId = entry.Key,
                    ServerName = entry.Value.Name,
                    ToolName = tool.Name,
                    Description = tool.Description,
                }).ToList()
                : [],
        }).ToList();
    }

    /// <summary>Resolves selected tools, or all plugin tools when references are null.</summary>
    public async Task<(IReadOnlyList<AITool> Tools, IReadOnlyList<string> Hints)> ResolveToolsAsync(
        IReadOnlyList<AgentToolReference>? references = null, CancellationToken ct = default,
        IEnumerable<string>? existingToolNames = null)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        references ??= _tools.SelectMany(entry => entry.Value.Select(tool =>
            new AgentToolReference { ServerId = entry.Key, ToolName = tool.Name })).ToList();
        var resolved = new List<AITool>();
        var names = new HashSet<string>(existingToolNames ?? [], StringComparer.Ordinal);
        var selectedProviders = new HashSet<string>(StringComparer.Ordinal);
        var hints = new List<string>();
        foreach (var reference in references)
        {
            var tool = _tools.GetValueOrDefault(reference.ServerId)?.FirstOrDefault(tool =>
                string.Equals(tool.Name, reference.ToolName, StringComparison.Ordinal));
            if (tool is null)
            {
                LogUnavailableTool(reference.ServerId, reference.ToolName);
                continue;
            }

            if (!names.Add(tool.Name))
            {
                LogDuplicateTool(reference.ServerId, tool.Name);
                continue;
            }

            resolved.Add(tool);
            if (selectedProviders.Add(reference.ServerId)
                && _providers[reference.ServerId].InstructionHint is { } hint
                && !string.IsNullOrWhiteSpace(hint))
            {
                hints.Add(hint.Trim());
            }
        }

        return (resolved, hints);
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await _initializationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var (id, provider) in _providers)
            {
                if (_tools.ContainsKey(id) || _errors.ContainsKey(id))
                {
                    continue;
                }

                try
                {
                    await provider.InitializeAsync(ct).ConfigureAwait(false);
                    _tools.Add(id, provider.GetTools());
                }
                catch (Exception ex) when (!ex.IsCancellation())
                {
                    _errors.Add(id, ex.Message);
                    LogProviderFailure(ex, id);
                }
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public void Dispose() => _initializationGate.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Plugin tool {ToolName} from {ProviderId} is unavailable")]
    private partial void LogUnavailableTool(string providerId, string toolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping duplicate plugin tool {ToolName} from {ProviderId}")]
    private partial void LogDuplicateTool(string providerId, string toolName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Plugin tool provider {ProviderId} failed to initialize")]
    private partial void LogProviderFailure(Exception exception, string providerId);
}
