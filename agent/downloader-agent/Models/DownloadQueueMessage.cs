namespace downloader_agent.Models;

public record DownloadQueueMessage(
    string DownloadId,
    string Url,
    DateTime EnqueuedAt
);
