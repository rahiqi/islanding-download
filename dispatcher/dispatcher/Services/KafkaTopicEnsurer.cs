using System.Text.RegularExpressions;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;

namespace dispatcher.Services;

public interface IKafkaTopicEnsurer
{
    /// <summary>Ensures download-queue, download-progress, and agent-heartbeat topics exist (idempotent).</summary>
    Task EnsureAgentTopicsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ensures the per-agent download queue topic exists (e.g. download-queue-AgentId). Call when an agent registers.</summary>
    Task EnsureAgentDownloadQueueTopicAsync(string agentId, CancellationToken cancellationToken = default);
}

public sealed class KafkaTopicEnsurer : IKafkaTopicEnsurer
{
    private static readonly Regex TopicNameSanitizer = new(@"[^a-zA-Z0-9._-]", RegexOptions.Compiled);

    private readonly string _bootstrapServers;
    private readonly string _downloadQueueTopic;
    private readonly string _progressTopic;
    private readonly string _heartbeatTopic;
    private readonly ILogger<KafkaTopicEnsurer> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _ensuredAgentTopics = new();
    private readonly SemaphoreSlim _agentTopicLock = new(1, 1);
    private bool _ensured;

    public KafkaTopicEnsurer(
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<KafkaConsumerOptions> consumerOptions,
        IOptions<AgentHeartbeatConsumerOptions> heartbeatOptions,
        ILogger<KafkaTopicEnsurer> logger)
    {
        _bootstrapServers = kafkaOptions.Value.BootstrapServers;
        _downloadQueueTopic = kafkaOptions.Value.DownloadQueueTopic;
        _progressTopic = consumerOptions.Value.ProgressTopic;
        _heartbeatTopic = heartbeatOptions.Value.HeartbeatTopic;
        _logger = logger;
    }

    public async Task EnsureAgentTopicsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ensured)
                return;

            var specs = new[]
            {
                new TopicSpecification { Name = _downloadQueueTopic, NumPartitions = 4, ReplicationFactor = 1 },
                new TopicSpecification { Name = _progressTopic, NumPartitions = 4, ReplicationFactor = 1 },
                new TopicSpecification { Name = _heartbeatTopic, NumPartitions = 2, ReplicationFactor = 1 }
            };

            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
            try
            {
                await admin.CreateTopicsAsync(specs, new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) }).ConfigureAwait(false);
                _logger.LogInformation("Kafka topics ensured: {Topics}", string.Join(", ", specs.Select(s => s.Name)));
            }
            catch (CreateTopicsException ex)
            {
                foreach (var result in ex.Results)
                {
                    if (result.Error?.Code == ErrorCode.TopicAlreadyExists)
                        _logger.LogDebug("Topic {Topic} already exists", result.Topic);
                    else
                        _logger.LogWarning(ex, "Failed to create topic {Topic}: {Reason}", result.Topic, result.Error?.Reason);
                }
                // Do not throw if some topics already exist; only fail on real errors
                if (ex.Results.Any(r => r.Error?.Code != ErrorCode.TopicAlreadyExists && r.Error?.Code != ErrorCode.NoError))
                    throw;
            }

            _ensured = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnsureAgentDownloadQueueTopicAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return;
        var topic = _downloadQueueTopic + "-" + TopicNameSanitizer.Replace(agentId.Trim(), "_");
        await _agentTopicLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ensuredAgentTopics.Contains(topic))
                return;
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
            try
            {
                await admin.CreateTopicsAsync(
                    new[] { new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 } },
                    new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) }).ConfigureAwait(false);
                _ensuredAgentTopics.Add(topic);
                _logger.LogInformation("Agent download queue topic ensured: {Topic}", topic);
            }
            catch (CreateTopicsException ex)
            {
                if (ex.Results.Any(r => r.Error?.Code == ErrorCode.TopicAlreadyExists))
                    _ensuredAgentTopics.Add(topic);
                else
                    throw;
            }
        }
        finally
        {
            _agentTopicLock.Release();
        }
    }
}
