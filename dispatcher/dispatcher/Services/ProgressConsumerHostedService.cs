using System.Text.Json;
using Confluent.Kafka;
using dispatcher.Models;
using Microsoft.Extensions.Options;

namespace dispatcher.Services;

public class KafkaConsumerOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ProgressTopic { get; set; } = "download-progress";
    public string GroupId { get; set; } = "dispatcher-progress";
}

public sealed class ProgressConsumerHostedService : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IDownloadStore _store;
    private readonly IProgressBroadcaster _broadcaster;
    private readonly ILogger<ProgressConsumerHostedService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ProgressConsumerHostedService(
        IOptions<KafkaConsumerOptions> options,
        IDownloadStore store,
        IProgressBroadcaster broadcaster,
        ILogger<ProgressConsumerHostedService> logger)
    {
        _store = store;
        _broadcaster = broadcaster;
        _logger = logger;
        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.GroupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(options.Value.ProgressTopic);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Progress consumer started for topic {Topic}", _consumer.Subscription.FirstOrDefault());
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = _consumer.Consume(stoppingToken);
                    if (cr.Message?.Value is not { } json)
                        continue;
                    var msg = JsonSerializer.Deserialize<DownloadProgressMessage>(json, JsonOptions);
                    if (msg is null)
                        continue;
                    _store.UpdateProgress(msg.DownloadId, msg.TotalBytes, msg.DownloadedBytes, msg.BytesPerSecond, msg.Status, msg.AgentId, msg.Message);
                    _broadcaster.Broadcast(msg);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Consume error");
                }
            }
        }
        finally
        {
            _consumer.Close();
            _consumer.Dispose();
        }
    }
}
