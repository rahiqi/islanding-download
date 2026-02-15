using System.Text.Json;
using Confluent.Kafka;
using dispatcher.Models;
using Microsoft.Extensions.Options;

namespace dispatcher.Services;

public class AgentHeartbeatConsumerOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string HeartbeatTopic { get; set; } = "agent-heartbeat";
    public string GroupId { get; set; } = "dispatcher-agents";
}

public sealed class AgentHeartbeatHostedService : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IAgentStore _agentStore;
    private readonly ILogger<AgentHeartbeatHostedService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AgentHeartbeatHostedService(
        IOptions<AgentHeartbeatConsumerOptions> options,
        IAgentStore agentStore,
        ILogger<AgentHeartbeatHostedService> logger)
    {
        _agentStore = agentStore;
        _logger = logger;
        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.GroupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(options.Value.HeartbeatTopic);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent heartbeat consumer started");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = _consumer.Consume(stoppingToken);
                    if (cr.Message?.Value is not { } json)
                        continue;
                    var msg = JsonSerializer.Deserialize<AgentHeartbeatMessage>(json, JsonOptions);
                    if (msg is null)
                        continue;
                    _agentStore.UpsertAgent(msg.AgentId, msg.LastSeen, msg.CurrentDownloads);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Agent heartbeat consume error");
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
