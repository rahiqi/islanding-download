using System.Text.Json;
using Confluent.Kafka;
using downloader_agent.Models;
using Microsoft.Extensions.Options;

namespace downloader_agent.Services;

public class HeartbeatOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string HeartbeatTopic { get; set; } = "agent-heartbeat";
    public int IntervalSeconds { get; set; } = 30;
}

public sealed class HeartbeatService : BackgroundService
{
    private readonly IProducer<string, string> _producer;
    private readonly DownloadWorkerService _worker;
    private readonly string _topic;
    private readonly int _intervalSeconds;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public HeartbeatService(
        IOptions<HeartbeatOptions> options,
        DownloadWorkerService worker,
        ILogger<HeartbeatService> logger)
    {
        _worker = worker;
        _logger = logger;
        _topic = options.Value.HeartbeatTopic;
        _intervalSeconds = options.Value.IntervalSeconds;
        var config = new ProducerConfig { BootstrapServers = options.Value.BootstrapServers };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SendHeartbeatAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
                await SendHeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat send failed");
            }
        }

        _producer.Dispose();
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var msg = new AgentHeartbeatMessage(_worker.AgentId, DateTime.UtcNow, _worker.CurrentDownloads);
        var json = JsonSerializer.Serialize(msg, _jsonOptions);
        await _producer.ProduceAsync(_topic, new Message<string, string> { Key = _worker.AgentId, Value = json }, ct);
    }
}
