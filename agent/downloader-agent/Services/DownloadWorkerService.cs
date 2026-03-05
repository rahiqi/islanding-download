using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Confluent.Kafka;
using downloader_agent.Models;
using Microsoft.Extensions.Options;

namespace downloader_agent.Services;

public class DownloadWorkerOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string DownloadQueueTopic { get; set; } = "download-queue";
    public string ProgressTopic { get; set; } = "download-progress";
    public string GroupId { get; set; } = "download-agents";
    public int ProgressIntervalMs { get; set; } = 200;
}

internal static class TopicNames
{
    private static readonly Regex Sanitize = new(@"[^a-zA-Z0-9._-]", RegexOptions.Compiled);
    public static string AgentSpecificTopic(string baseTopic, string agentId) =>
        baseTopic + "-" + Sanitize.Replace(agentId, "_");
}

public sealed class DownloadWorkerService : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IProducer<string, string> _progressProducer;
    private readonly HttpClient _httpClient;
    private readonly string _agentId;
    private readonly string _progressTopic;
    private readonly int _progressIntervalMs;
    private readonly ILogger<DownloadWorkerService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private int _currentDownloads;

    public DownloadWorkerService(
        IOptions<DownloadWorkerOptions> options,
        IOptions<AgentOptions> agentOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<DownloadWorkerService> logger)
    {
        _logger = logger;
        _agentId = agentOptions.Value.AgentId!;
        _progressTopic = options.Value.ProgressTopic;
        _progressIntervalMs = options.Value.ProgressIntervalMs;
        _logger.LogInformation("Kafka BootstrapServers: {BootstrapServers}", options.Value.BootstrapServers);
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        var baseTopic = options.Value.DownloadQueueTopic;
        var topics = new List<string> { baseTopic, TopicNames.AgentSpecificTopic(baseTopic, _agentId) };
        _consumer.Subscribe(topics);

        var producerConfig = new ProducerConfig { BootstrapServers = options.Value.BootstrapServers };
        _progressProducer = new ProducerBuilder<string, string>(producerConfig).Build();

        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromHours(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Download worker started. AgentId={AgentId}", _agentId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = _consumer.Consume(stoppingToken);
                    if (cr.Message?.Value is not { } json)
                        continue;
                    var msg = JsonSerializer.Deserialize<DownloadQueueMessage>(json, _jsonOptions);
                    if (msg is null)
                        continue;
                    await ProcessDownloadAsync(msg, stoppingToken);
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
            _progressProducer.Dispose();
        }
    }

    private async Task ProcessDownloadAsync(DownloadQueueMessage msg, CancellationToken ct)
    {
        Interlocked.Increment(ref _currentDownloads);
        try
        {
            _logger.LogInformation("Starting download {DownloadId}: {Url}", msg.DownloadId, msg.Url);
            await DownloadWithProgressAsync(msg.DownloadId, msg.Url, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed {DownloadId}", msg.DownloadId);
            await SendProgressAsync(msg.DownloadId, 0, 0, 0, "Failed", ex.Message, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _currentDownloads);
        }
    }

    private async Task DownloadWithProgressAsync(string downloadId, string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await SendProgressAsync(downloadId, 0, 0, 0, "Downloading", null, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long downloaded = 0;
        var sw = Stopwatch.StartNew();
        var lastProgressSend = Stopwatch.StartNew();

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            downloaded += read;
            var elapsed = sw.Elapsed.TotalSeconds;
            var rate = elapsed > 0 ? downloaded / elapsed : 0;

            if (lastProgressSend.ElapsedMilliseconds >= _progressIntervalMs)
            {
                await SendProgressAsync(downloadId, totalBytes, downloaded, rate, "Downloading", null, ct);
                lastProgressSend.Restart();
            }
        }

        var finalRate = sw.Elapsed.TotalSeconds > 0 ? downloaded / sw.Elapsed.TotalSeconds : 0;
        await SendProgressAsync(downloadId, totalBytes ?? downloaded, downloaded, finalRate, "Completed", null, ct);
        _logger.LogInformation("Completed download {DownloadId}: {Bytes} bytes", downloadId, downloaded);
    }

    private async Task SendProgressAsync(string downloadId, long? totalBytes, long downloadedBytes, double bytesPerSecond, string status, string? message, CancellationToken ct)
    {
        var msg = new DownloadProgressMessage(downloadId, _agentId, totalBytes, downloadedBytes, bytesPerSecond, status, message, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(msg, _jsonOptions);
        await _progressProducer.ProduceAsync(_progressTopic, new Message<string, string> { Key = downloadId, Value = json }, ct);
    }

    public int CurrentDownloads => _currentDownloads;
    public string AgentId => _agentId;
}
