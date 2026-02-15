namespace dispatcher.Models;

/// <summary>Produced to download-queue when user submits a URL.</summary>
public record DownloadQueueMessage(
    string DownloadId,
    string Url,
    DateTime EnqueuedAt
);

/// <summary>Consumed from download-progress; produced by agents.</summary>
public record DownloadProgressMessage(
    string DownloadId,
    string AgentId,
    long? TotalBytes,
    long DownloadedBytes,
    double BytesPerSecond,
    string Status, // Downloading, Completed, Failed
    string? Message = null,
    DateTime? Timestamp = null
);

/// <summary>Consumed from agent-heartbeat.</summary>
public record AgentHeartbeatMessage(
    string AgentId,
    DateTime LastSeen,
    int CurrentDownloads = 0
);
