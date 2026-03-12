using System.Collections.Concurrent;
using dispatcher.Models;

namespace dispatcher.Services;

public sealed class DownloadStore : IDownloadStore
{
    private readonly ConcurrentDictionary<string, DownloadState> _downloads = new();
    private readonly ConcurrentDictionary<string, string?> _requestedActions = new();

    public void Add(DownloadState state) => _downloads[state.DownloadId] = state;

    public DownloadState? Get(string downloadId) => _downloads.TryGetValue(downloadId, out var s) ? s : null;

    public IReadOnlyList<DownloadState> GetAll() => _downloads.Values.OrderByDescending(d => d.EnqueuedAt).ToList();

    public void SetRequestedAction(string downloadId, string? action) =>
        _requestedActions[downloadId] = string.IsNullOrEmpty(action) ? null : action;

    public string? GetRequestedAction(string downloadId) =>
        _requestedActions.TryGetValue(downloadId, out var a) ? a : null;

    public void UpdateProgress(string downloadId, long? totalBytes, long downloadedBytes, double bytesPerSecond, string status, string? agentId = null, string? message = null, string? localDownloadUrl = null)
    {
        if (!_downloads.TryGetValue(downloadId, out var existing))
            return;

        if (status is "Paused" or "Cancelled")
            _requestedActions.TryRemove(downloadId, out _);

        var updated = existing with
        {
            TotalBytes = totalBytes ?? existing.TotalBytes,
            DownloadedBytes = downloadedBytes,
            BytesPerSecond = bytesPerSecond,
            Status = status,
            AgentId = agentId ?? existing.AgentId,
            ErrorMessage = message ?? existing.ErrorMessage,
            CompletedAt = status is "Completed" or "Failed" or "Cancelled" ? DateTime.UtcNow : existing.CompletedAt,
            LocalDownloadUrl = localDownloadUrl ?? existing.LocalDownloadUrl
        };
        _downloads[downloadId] = updated;
    }
}
