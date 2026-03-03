using System.Text.Json;
using System.Text.RegularExpressions;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
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
}

public sealed class KafkaDownloadProducer : IKafkaDownloadProducer
{
    private readonly IProducer<string, string> _producer;
    private readonly IAdminClient _adminClient;
    private readonly string _baseTopic;
    private readonly HashSet<string> _ensuredTopics = new();
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex TopicNameSanitizer = new(@"[^a-zA-Z0-9._-]", RegexOptions.Compiled);

    public KafkaDownloadProducer(IOptions<KafkaOptions> options)
    {
        var bootstrapServers = options.Value.BootstrapServers;
        _baseTopic = options.Value.DownloadQueueTopic;
        var config = new ProducerConfig { BootstrapServers = bootstrapServers };
        _producer = new ProducerBuilder<string, string>(config).Build();
        _adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
    }

    public async Task EnqueueAsync(string downloadId, string url, string? preferredAgentId = null, CancellationToken cancellationToken = default)
    {
        var topic = string.IsNullOrWhiteSpace(preferredAgentId)
            ? _baseTopic
            : AgentTopic(_baseTopic, preferredAgentId.Trim());

        await EnsureTopicExistsAsync(topic).ConfigureAwait(false);

        var message = new DownloadQueueMessage(downloadId, url, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _producer.ProduceAsync(topic, new Message<string, string> { Key = downloadId, Value = json }, cancellationToken).ConfigureAwait(false);
    }

    private static string AgentTopic(string baseTopic, string agentId) =>
        baseTopic + "-" + TopicNameSanitizer.Replace(agentId, "_");

    private async Task EnsureTopicExistsAsync(string topic)
    {
        if (topic == _baseTopic)
            return;
        await _ensureLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_ensuredTopics.Contains(topic))
                return;
            try
            {
                await _adminClient.CreateTopicsAsync(new[] { new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 } }, null).ConfigureAwait(false);
                _ensuredTopics.Add(topic);
            }
            catch (CreateTopicsException ex)
            {
                if (ex.Results.Any(r => r.Error?.Code == ErrorCode.TopicAlreadyExists))
                    _ensuredTopics.Add(topic);
                else
                    throw;
            }
        }
        finally
        {
            _ensureLock.Release();
        }
    }
}
