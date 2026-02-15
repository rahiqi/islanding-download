namespace downloader_agent.Models;

public record AgentHeartbeatMessage(
    string AgentId,
    DateTime LastSeen,
    int CurrentDownloads = 0
);
