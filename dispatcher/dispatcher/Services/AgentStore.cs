using System.Collections.Concurrent;
using dispatcher.Models;

namespace dispatcher.Services;

public sealed class AgentStore : IAgentStore
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();

    public void UpsertAgent(string agentId, DateTime lastSeen, int currentDownloads) =>
        _agents[agentId] = new AgentInfo(agentId, lastSeen, currentDownloads);

    public IReadOnlyList<AgentInfo> GetAvailableAgents(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        return _agents.Values.Where(a => a.LastSeen >= cutoff).OrderBy(a => a.AgentId).ToList();
    }
}
