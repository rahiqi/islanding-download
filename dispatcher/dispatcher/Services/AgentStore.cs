using System.Collections.Concurrent;
using dispatcher.Models;

namespace dispatcher.Services;

public sealed class AgentStore : IAgentStore
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();

    public void RegisterAgent(string agentId, string name, string location)
    {
        var now = DateTime.UtcNow;
        _agents.AddOrUpdate(agentId,
            _ => new AgentInfo(agentId, name ?? "", location ?? "", now, 0),
            (_, existing) => existing with { Name = name ?? existing.Name, Location = location ?? existing.Location });
    }

    public void UpsertAgent(string agentId, DateTime lastSeen, int currentDownloads)
    {
        _agents.AddOrUpdate(agentId,
            _ => new AgentInfo(agentId, "", "", lastSeen, currentDownloads),
            (_, existing) => existing with { LastSeen = lastSeen, CurrentDownloads = currentDownloads });
    }

    public IReadOnlyList<AgentInfo> GetAvailableAgents(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        return _agents.Values.Where(a => a.LastSeen >= cutoff).OrderBy(a => a.AgentId).ToList();
    }
}
