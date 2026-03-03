namespace dispatcher.Services;

public interface IAgentStore
{
    void RegisterAgent(string agentId, string name, string location);
    void UpsertAgent(string agentId, DateTime lastSeen, int currentDownloads);
    IReadOnlyList<dispatcher.Models.AgentInfo> GetAvailableAgents(TimeSpan maxAge);
}
