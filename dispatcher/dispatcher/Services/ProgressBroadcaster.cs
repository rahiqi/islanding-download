using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using dispatcher.Models;

namespace dispatcher.Services;

public sealed class ProgressBroadcaster : IProgressBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<DownloadProgressMessage>> _subscribers = new();

    public void Broadcast(DownloadProgressMessage message)
    {
        foreach (var ch in _subscribers.Values)
        {
            try
            {
                ch.Writer.TryWrite(message);
            }
            catch
            {
                // ignore per-subscriber errors
            }
        }
    }

    public async IAsyncEnumerable<DownloadProgressMessage> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<DownloadProgressMessage>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        try
        {
            _subscribers[id] = channel;
            await foreach (var msg in channel.Reader.ReadAllAsync(cancellationToken))
                yield return msg;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}
