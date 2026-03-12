using System.Text.Json;
using System.Text.RegularExpressions;
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
    Task EnqueueAsync(string downloadId, string url, string? preferredAgentId = null, CancellationToken cancellationToken = default);
    Task EnqueueResumeAsync(string downloadId, string url, long startByte, string agentId, CancellationToken cancellationToken = default);
}

public sealed class KafkaDownloadProducer : IKafkaDownloadProducer
{
    private readonly IProducer<string, string> _producer;
    private readonly string _baseTopic;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex TopicNameSanitizer = new(@"[^a-zA-Z0-9._-]", RegexOptions.Compiled);

    public KafkaDownloadProducer(IOptions<KafkaOptions> options)
    {
        var bootstrapServers = options.Value.BootstrapServers;
        _baseTopic = options.Value.DownloadQueueTopic;
        var config = new ProducerConfig { BootstrapServers = bootstrapServers };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task EnqueueAsync(string downloadId, string url, string? preferredAgentId = null, CancellationToken cancellationToken = default)
    {
        var topic = string.IsNullOrWhiteSpace(preferredAgentId)
            ? _baseTopic
            : AgentTopic(_baseTopic, preferredAgentId.Trim());

        var message = new DownloadQueueMessage(downloadId, url, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _producer.ProduceAsync(topic, new Message<string, string> { Key = downloadId, Value = json }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueResumeAsync(string downloadId, string url, long startByte, string agentId, CancellationToken cancellationToken = default)
    {
        var topic = AgentTopic(_baseTopic, agentId);
        var message = new DownloadQueueMessage(downloadId, url, DateTime.UtcNow, startByte);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _producer.ProduceAsync(topic, new Message<string, string> { Key = downloadId, Value = json }, cancellationToken).ConfigureAwait(false);
    }

    private static string AgentTopic(string baseTopic, string agentId) =>
        baseTopic + "-" + TopicNameSanitizer.Replace(agentId, "_");
}
