namespace dispatcher.Services;

public interface IAgentStore
{
    void UpsertAgent(string agentId, DateTime lastSeen, int currentDownloads);
    IReadOnlyList<dispatcher.Models.AgentInfo> GetAvailableAgents(TimeSpan maxAge);
}
