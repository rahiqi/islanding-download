namespace downloader_agent.Models;

public record AgentHeartbeatMessage(
    string AgentId,
    DateTime LastSeen,
    int CurrentDownloads = 0,
    long? DownloadDiskTotalBytes = null,
    long? DownloadDiskFreeBytes = null,
    long? DownloadDiskUsedBytes = null
);
