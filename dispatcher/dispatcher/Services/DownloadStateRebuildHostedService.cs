using System.Text.Json;
using Confluent.Kafka;
using dispatcher.Models;
using Microsoft.Extensions.Options;

namespace dispatcher.Services;

public class DownloadStateRebuildOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string StateTopic { get; set; } = "download-state";
    public string GroupId { get; set; } = "dispatcher-download-state-rebuild";
    /// <summary>How long to wait with no messages before assuming we've reached the end of the log.</summary>
    public int IdleTimeoutSeconds { get; set; } = 5;
}

/// <summary>
/// One-shot service that replays the download-state Kafka topic on startup
/// to rebuild the in-memory DownloadStore after dispatcher restarts.
/// </summary>
public sealed class DownloadStateRebuildHostedService : BackgroundService
{
    private readonly IDownloadStore _store;
    private readonly ILogger<DownloadStateRebuildHostedService> _logger;
    private readonly IConsumer<string, string> _consumer;
    private readonly string _topic;
    private readonly int _idleTimeoutSeconds;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public DownloadStateRebuildHostedService(
        IOptions<DownloadStateRebuildOptions> options,
        IDownloadStore store,
        ILogger<DownloadStateRebuildHostedService> logger)
    {
        _store = store;
        _logger = logger;
        _topic = options.Value.StateTopic;
        _idleTimeoutSeconds = options.Value.IdleTimeoutSeconds;

        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            // Always use a fresh consumer group so we replay the topic from the beginning on each restart.
            GroupId = $"{options.Value.GroupId}-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(_topic);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private void RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rebuilding download state from topic {Topic}", _topic);
        var idleSince = DateTime.UtcNow;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = _consumer.Consume(TimeSpan.FromSeconds(1));
                    if (cr == null)
                    {
                        if ((DateTime.UtcNow - idleSince).TotalSeconds >= _idleTimeoutSeconds)
                        {
                            _logger.LogInformation("Download state rebuild reached end of topic {Topic}", _topic);
                            break;
                        }
                        continue;
                    }

                    idleSince = DateTime.UtcNow;

                    if (cr.Message?.Value is not { } json)
                        continue;

                    var state = JsonSerializer.Deserialize<DownloadState>(json, JsonOptions);
                    if (state is null)
                        continue;

                    _store.Add(state);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Download state rebuild consume error");
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

