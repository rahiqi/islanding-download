using System.Diagnostics;
using System.Net.Http.Headers;
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
    /// <summary>Directory where completed downloads are saved (e.g. ./downloads).</summary>
    public string DownloadPath { get; set; } = "downloads";
}

internal static class TopicNames
{
    private static readonly Regex Sanitize = new(@"[^a-zA-Z0-9._-]", RegexOptions.Compiled);
    public static string AgentSpecificTopic(string baseTopic, string agentId) =>
        baseTopic + "-" + Sanitize.Replace(agentId, "_");
}

internal static class DownloadFileName
{
    private static readonly Regex SanitizeFileName = new(@"[^\w\s.\-()\[\]&'+]", RegexOptions.Compiled);
    private static readonly Regex CollapseSpaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Resolves a safe file name from response Content-Disposition and URL path; falls back to fallbackId if none.</summary>
    public static string Resolve(HttpContentHeaders contentHeaders, string url, string fallbackId)
    {
        var fromHeader = GetFileNameFromContentDisposition(contentHeaders.ContentDisposition);
        if (!string.IsNullOrWhiteSpace(fromHeader))
            return Sanitize(fromHeader);
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segment = uri.Segments.Length > 0 ? uri.Segments[^1] : null;
            var fromUrl = string.IsNullOrEmpty(segment) ? null : Uri.UnescapeDataString(segment).Trim();
            if (!string.IsNullOrWhiteSpace(fromUrl))
                return Sanitize(fromUrl);
        }
        return fallbackId;
    }

    private static string? GetFileNameFromContentDisposition(ContentDispositionHeaderValue? contentDisposition)
    {
        if (contentDisposition == null)
            return null;
        if (!string.IsNullOrWhiteSpace(contentDisposition.FileName))
            return contentDisposition.FileName.Trim().Trim('"');
        if (contentDisposition.ToString() is { } raw)
        {
            var star = Regex.Match(raw, @"filename\*\s*=\s*(?:UTF-8'')?([^;\s]+)", RegexOptions.IgnoreCase);
            if (star.Success)
                return Uri.UnescapeDataString(star.Groups[1].Value.Trim().Trim('"'));
        }
        return null;
    }

    private static string Sanitize(string name)
    {
        var noPath = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(noPath))
            return "download";
        var sanitized = SanitizeFileName.Replace(noPath, "_");
        sanitized = CollapseSpaces.Replace(sanitized.Trim(), " ");
        return string.IsNullOrWhiteSpace(sanitized) || sanitized == "." ? "download" : sanitized;
    }
}

public sealed class DownloadWorkerService : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IProducer<string, string> _progressProducer;
    private readonly HttpClient _httpClient;
    private readonly string _agentId;
    private readonly string _progressTopic;
    private readonly int _progressIntervalMs;
    private readonly string _downloadPath;
    private readonly string _localServeBaseUrl;
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
        _downloadPath = Path.GetFullPath(options.Value.DownloadPath);
        _localServeBaseUrl = (agentOptions.Value.LocalServeBaseUrl ?? "").TrimEnd('/');
        Directory.CreateDirectory(_downloadPath);
        _logger.LogInformation("Kafka BootstrapServers: {BootstrapServers}, DownloadPath: {DownloadPath}", options.Value.BootstrapServers, _downloadPath);
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

        _httpClient = httpClientFactory.CreateClient(/*ChromeDownloadHeaders.HttpClientName*/);
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
            await SendProgressAsync(msg.DownloadId, 0, 0, 0, "Failed", ex.Message, null, ct);
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
        var fileName = DownloadFileName.Resolve(response.Content.Headers, url, downloadId);
        var dirPath = Path.Combine(_downloadPath, downloadId);
        Directory.CreateDirectory(dirPath);
        var filePath = Path.Combine(dirPath, fileName);

        await SendProgressAsync(downloadId, 0, 0, 0, "Downloading", null, null, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920];
        long downloaded = 0;
        var sw = Stopwatch.StartNew();
        var lastProgressSend = Stopwatch.StartNew();

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            var elapsed = sw.Elapsed.TotalSeconds;
            var rate = elapsed > 0 ? downloaded / elapsed : 0;

            if (lastProgressSend.ElapsedMilliseconds >= _progressIntervalMs)
            {
                await SendProgressAsync(downloadId, totalBytes, downloaded, rate, "Downloading", null, null, ct);
                lastProgressSend.Restart();
            }
        }

        var finalRate = sw.Elapsed.TotalSeconds > 0 ? downloaded / sw.Elapsed.TotalSeconds : 0;
        var localUrl = string.IsNullOrEmpty(_localServeBaseUrl) ? null : $"{_localServeBaseUrl}/downloads/{downloadId}";
        await SendProgressAsync(downloadId, totalBytes ?? downloaded, downloaded, finalRate, "Completed", null, localUrl, ct);
        _logger.LogInformation("Completed download {DownloadId}: {Bytes} bytes, local URL: {LocalUrl}", downloadId, downloaded, localUrl ?? "(none)");
    }

    private async Task SendProgressAsync(string downloadId, long? totalBytes, long downloadedBytes, double bytesPerSecond, string status, string? message, string? localDownloadUrl, CancellationToken ct)
    {
        var msg = new DownloadProgressMessage(downloadId, _agentId, totalBytes, downloadedBytes, bytesPerSecond, status, message, DateTime.UtcNow, localDownloadUrl);
        var json = JsonSerializer.Serialize(msg, _jsonOptions);
        await _progressProducer.ProduceAsync(_progressTopic, new Message<string, string> { Key = downloadId, Value = json }, ct);
    }

    public int CurrentDownloads => _currentDownloads;
    public string AgentId => _agentId;
}
