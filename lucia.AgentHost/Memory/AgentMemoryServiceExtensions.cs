using AgentMemory.Abstractions.Options;
using AgentMemory.AgentFramework;
using AgentMemory.Core;
using AgentMemory.Extraction.Llm;
using AgentMemory.Neo4j.Infrastructure;
using lucia.Agents.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace lucia.AgentHost.Memory;

public static class AgentMemoryServiceExtensions
{
    /// <summary>
    /// Replaces the built-in user memory provider with Neo4j-backed Agent Memory when
    /// <c>AgentMemory:Enabled</c> is true. Does nothing otherwise.
    /// </summary>
    public static IHostApplicationBuilder AddAgentMemory(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AgentMemoryOptions.SectionName).Get<AgentMemoryOptions>()
            ?? new AgentMemoryOptions();
        if (!options.Enabled)
        {
            return builder;
        }

        var services = builder.Services;
        // Fail closed: an operation without an owner throws instead of reading or writing global memory.
        services.AddAgentMemoryCore(memory => memory.Isolation.Mode = MemoryIsolationMode.StrictMultiTenant);
        services.AddNeo4jAgentMemory(neo4j =>
        {
            neo4j.Uri = options.Uri;
            neo4j.Username = options.Username;
            neo4j.Password = options.Password;
            neo4j.Database = options.Database;
            neo4j.EmbeddingDimensions = options.EmbeddingDimensions;
        });
        services.AddLlmExtraction();
        services.AddAgentMemoryFramework();

        // Agent Memory takes these from DI; Lucia resolves them from its model provider system.
        // ponytail: resolved once per process, so a model provider change needs a restart.
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<IChatClientResolver>()
            .ResolveAsync(NullIfBlank(options.ModelConnectionName)).GetAwaiter().GetResult());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            sp.GetRequiredService<IEmbeddingProviderResolver>()
                .ResolveAsync(NullIfBlank(options.EmbeddingProviderName)).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Agent Memory needs an embedding provider. Add one under Model Providers."));

        services.AddSingleton<AgentMemoryContextProvider>();
        services.Replace(ServiceDescriptor.Singleton<AIContextProvider>(sp => sp.GetRequiredService<AgentMemoryContextProvider>()));
        services.AddHostedService(sp => sp.GetRequiredService<AgentMemoryContextProvider>());
        return builder;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
