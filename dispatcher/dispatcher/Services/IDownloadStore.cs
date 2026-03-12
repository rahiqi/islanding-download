namespace dispatcher.Services;

public interface IDownloadStore
{
    void Add(Models.DownloadState state);
    Models.DownloadState? Get(string downloadId);
    IReadOnlyList<Models.DownloadState> GetAll();
    void UpdateProgress(string downloadId, long? totalBytes, long downloadedBytes, double bytesPerSecond, string status, string? agentId = null, string? message = null, string? localDownloadUrl = null);
    void SetRequestedAction(string downloadId, string? action); // "pause", "cancel", or null to clear
    string? GetRequestedAction(string downloadId);
}
