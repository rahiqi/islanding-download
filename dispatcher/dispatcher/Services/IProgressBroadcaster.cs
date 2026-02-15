namespace dispatcher.Services;

public interface IProgressBroadcaster
{
    void Broadcast(dispatcher.Models.DownloadProgressMessage message);
    IAsyncEnumerable<dispatcher.Models.DownloadProgressMessage> Subscribe(CancellationToken cancellationToken = default);
}
