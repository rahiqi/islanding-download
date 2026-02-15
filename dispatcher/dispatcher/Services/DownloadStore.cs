using System.Collections.Concurrent;
using dispatcher.Models;

namespace dispatcher.Services;

public sealed class DownloadStore : IDownloadStore
{
    private readonly ConcurrentDictionary<string, DownloadState> _downloads = new();

    public void Add(DownloadState state) => _downloads[state.DownloadId] = state;

    public DownloadState? Get(string downloadId) => _downloads.TryGetValue(downloadId, out var s) ? s : null;

    public IReadOnlyList<DownloadState> GetAll() => _downloads.Values.OrderByDescending(d => d.EnqueuedAt).ToList();

    public void UpdateProgress(string downloadId, long? totalBytes, long downloadedBytes, double bytesPerSecond, string status, string? agentId = null, string? message = null)
    {
        if (!_downloads.TryGetValue(downloadId, out var existing))
            return;

        var updated = existing with
        {
            TotalBytes = totalBytes ?? existing.TotalBytes,
            DownloadedBytes = downloadedBytes,
            BytesPerSecond = bytesPerSecond,
            Status = status,
            AgentId = agentId ?? existing.AgentId,
            ErrorMessage = message ?? existing.ErrorMessage,
            CompletedAt = status is "Completed" or "Failed" ? DateTime.UtcNow : existing.CompletedAt
        };
        _downloads[downloadId] = updated;
    }
}
