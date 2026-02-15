namespace dispatcher.Models;

public record DownloadState(
    string DownloadId,
    string Url,
    string Status, // Queued, Downloading, Completed, Failed
    DateTime EnqueuedAt,
    long TotalBytes = 0,
    long DownloadedBytes = 0,
    double BytesPerSecond = 0,
    string? AgentId = null,
    string? ErrorMessage = null,
    DateTime? CompletedAt = null
)
{
    public double PercentComplete =>
        TotalBytes > 0 ? Math.Min(100, 100.0 * DownloadedBytes / TotalBytes) : 0;
}
