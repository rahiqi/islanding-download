using System.Text.Json;
using Confluent.Kafka;
using dispatcher.Models;
using Microsoft.Extensions.Options;

namespace dispatcher.Services;

public class KafkaOptions
{
    public const string SectionName = "Kafka";
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string DownloadQueueTopic { get; set; } = "download-queue";
}

public interface IKafkaDownloadProducer
{
    Task EnqueueAsync(string downloadId, string url, CancellationToken cancellationToken = default);
}

public sealed class KafkaDownloadProducer : IKafkaDownloadProducer
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public KafkaDownloadProducer(IOptions<KafkaOptions> options)
    {
        var config = new ProducerConfig { BootstrapServers = options.Value.BootstrapServers };
        _producer = new ProducerBuilder<string, string>(config).Build();
        _topic = options.Value.DownloadQueueTopic;
    }

    public async Task EnqueueAsync(string downloadId, string url, CancellationToken cancellationToken = default)
    {
        var message = new DownloadQueueMessage(downloadId, url, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _producer.ProduceAsync(_topic, new Message<string, string> { Key = downloadId, Value = json }, cancellationToken);
    }

}
