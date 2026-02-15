namespace dispatcher.Models;

public record AgentInfo(
    string AgentId,
    DateTime LastSeen,
    int CurrentDownloads = 0
);
