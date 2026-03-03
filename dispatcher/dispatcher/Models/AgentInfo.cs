namespace dispatcher.Models;

public record AgentInfo(
    string AgentId,
    string Name,
    string Location,
    DateTime LastSeen,
    int CurrentDownloads = 0
);
