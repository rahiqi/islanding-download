namespace dispatcher.Services;

public interface IAgentStore
{
    void RegisterAgent(string agentId, string name, string location);
    void UpsertAgent(string agentId, DateTime lastSeen, int currentDownloads, long? totalBytes = null, long? freeBytes = null, long? usedBytes = null);
    IReadOnlyList<dispatcher.Models.AgentInfo> GetAvailableAgents(TimeSpan maxAge);
}
