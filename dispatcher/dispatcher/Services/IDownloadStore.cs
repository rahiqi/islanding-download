namespace dispatcher.Services;

public interface IDownloadStore
{
    void Add(Models.DownloadState state);
    Models.DownloadState? Get(string downloadId);
    IReadOnlyList<Models.DownloadState> GetAll();
    void UpdateProgress(string downloadId, long? totalBytes, long downloadedBytes, double bytesPerSecond, string status, string? agentId = null, string? message = null);
}
