namespace downloader_agent.Models;

public record DownloadProgressMessage(
    string DownloadId,
    string AgentId,
    long? TotalBytes,
    long DownloadedBytes,
    double BytesPerSecond,
    string Status,
    string? Message = null,
    DateTime? Timestamp = null
);
