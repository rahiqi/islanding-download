using System.Text.Json;
using Confluent.Kafka;
using dispatcher.Models;
using Microsoft.Extensions.Options;

namespace dispatcher.Services;

public interface IKafkaDownloadStateProducer
{
    Task SaveAsync(DownloadState state, CancellationToken cancellationToken = default);
}

public sealed class KafkaDownloadStateProducer : IKafkaDownloadStateProducer
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public KafkaDownloadStateProducer(IOptions<KafkaOptions> options)
    {
        var config = new ProducerConfig { BootstrapServers = options.Value.BootstrapServers };
        _producer = new ProducerBuilder<string, string>(config).Build();
        _topic = string.IsNullOrWhiteSpace(options.Value.DownloadStateTopic)
            ? "download-state"
            : options.Value.DownloadStateTopic;
    }

    public async Task SaveAsync(DownloadState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = state.DownloadId,
            Value = json
        }, cancellationToken).ConfigureAwait(false);
    }
}

