using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Mcp;

using Microsoft.EntityFrameworkCore;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAgentDefinitionRepository"/>.
/// Manages both <see cref="AgentDefinition"/> and <see cref="McpToolServerDefinition"/> entities.
/// </summary>
public sealed class EfAgentDefinitionRepository(IDbContextFactory<LuciaDbContext> dbFactory) : IAgentDefinitionRepository
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    // ── MCP Tool Servers ────────────────────────────────────────

    public async Task<List<McpToolServerDefinition>> GetAllToolServersAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.McpToolServers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<McpToolServerDefinition?> GetToolServerAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.McpToolServers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertToolServerAsync(McpToolServerDefinition server, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        server.UpdatedAt = DateTime.UtcNow;
        var existing = await db.McpToolServers.FindAsync([server.Id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Entry(existing).CurrentValues.SetValues(server);
        }
        else
        {
            db.McpToolServers.Add(server);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteToolServerAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.McpToolServers.FindAsync([id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.McpToolServers.Remove(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    // ── Agent Definitions ───────────────────────────────────────

    public async Task<List<AgentDefinition>> GetAllAgentDefinitionsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AgentDefinitions
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<AgentDefinition>> GetEnabledAgentDefinitionsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AgentDefinitions
            .AsNoTracking()
            .Where(a => a.Enabled)
            .OrderBy(a => a.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<AgentDefinition?> GetAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AgentDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAgentDefinitionAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        definition.UpdatedAt = DateTime.UtcNow;
        var existing = await db.AgentDefinitions.FindAsync([definition.Id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Entry(existing).CurrentValues.SetValues(definition);
        }
        else
        {
            db.AgentDefinitions.Add(definition);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.AgentDefinitions.FindAsync([id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.AgentDefinitions.Remove(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
