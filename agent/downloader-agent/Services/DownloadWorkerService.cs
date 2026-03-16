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

    /// <summary>Max parallel connections per download (IDM-style). Only used when server supports Range and file is large enough.</summary>
    public int MaxDownloadConnections { get; set; } = 8;
    /// <summary>Minimum file size (bytes) to use multi-connection. Smaller files use a single connection.</summary>
    public long MinSizeForMultiConnectionBytes { get; set; } = 1_000_000; // 1 MB
    /// <summary>Minimum segment size (bytes). Segments smaller than this are not used.</summary>
    public long MinSegmentSizeBytes { get; set; } = 262_144; // 256 KB
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
    private readonly int _maxDownloadConnections;
    private readonly long _minSizeForMultiConnection;
    private readonly long _minSegmentSizeBytes;
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
        _maxDownloadConnections = Math.Clamp(options.Value.MaxDownloadConnections, 1, 32);
        _minSizeForMultiConnection = Math.Max(0, options.Value.MinSizeForMultiConnectionBytes);
        _minSegmentSizeBytes = Math.Max(65536, options.Value.MinSegmentSizeBytes); // at least 64 KB per segment
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
            await DownloadWithProgressAsync(msg.DownloadId, msg.Url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed {DownloadId}", msg.DownloadId);
            await SendProgressAsync(msg.DownloadId, 0, 0, 0, "Failed", ex.Message, null, ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _currentDownloads);
        }
    }

    private async Task DownloadWithProgressAsync(string downloadId, string url, CancellationToken ct)
    {
        var (totalBytes, supportsRanges, contentHeaders) = await ProbeDownloadAsync(url, ct).ConfigureAwait(false);
        var fileName = DownloadFileName.Resolve(contentHeaders, url, downloadId);
        var dirPath = Path.Combine(_downloadPath, downloadId);
        Directory.CreateDirectory(dirPath);
        var filePath = Path.Combine(dirPath, fileName);

        var useMultiConnection = totalBytes.HasValue
            && totalBytes.Value >= _minSizeForMultiConnection
            && supportsRanges
            && totalBytes.Value > 0;

        if (useMultiConnection)
        {
            var numSegments = (int)Math.Min(_maxDownloadConnections, (totalBytes!.Value + _minSegmentSizeBytes - 1) / _minSegmentSizeBytes);
            numSegments = Math.Max(1, numSegments);
            _logger.LogInformation("Download {DownloadId}: multi-connection with {Connections} segments", downloadId, numSegments);
            await DownloadMultiConnectionAsync(downloadId, url, filePath, totalBytes!.Value, numSegments, ct).ConfigureAwait(false);
        }
        else
        {
            await DownloadSingleConnectionAsync(downloadId, url, filePath, totalBytes, ct).ConfigureAwait(false);
        }

        var localUrl = string.IsNullOrEmpty(_localServeBaseUrl) ? null : $"{_localServeBaseUrl}/downloads/{downloadId}";
        await SendProgressAsync(downloadId, totalBytes, totalBytes ?? 0, 0, "Completed", null, localUrl, ct).ConfigureAwait(false);
        _logger.LogInformation("Completed download {DownloadId}, local URL: {LocalUrl}", downloadId, localUrl ?? "(none)");
    }

    /// <summary>HEAD request to get size, range support, and headers (e.g. for filename).</summary>
    private async Task<(long? TotalBytes, bool SupportsRanges, HttpContentHeaders ContentHeaders)> ProbeDownloadAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        var supportsRanges = response.Headers.AcceptRanges?.Contains("bytes", StringComparer.OrdinalIgnoreCase) == true;
        return (totalBytes, supportsRanges, response.Content.Headers);
    }

    private async Task DownloadSingleConnectionAsync(string downloadId, string url, string filePath, long? totalBytes, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        totalBytes ??= response.Content.Headers.ContentLength;

        await SendProgressAsync(downloadId, totalBytes, 0, 0, "Downloading", null, null, ct).ConfigureAwait(false);

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, FileOptions.Asynchronous);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[81920];
        long downloaded = 0;
        var sw = Stopwatch.StartNew();
        var lastProgressSend = Stopwatch.StartNew();

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            var elapsed = sw.Elapsed.TotalSeconds;
            var rate = elapsed > 0 ? downloaded / elapsed : 0;

            if (lastProgressSend.ElapsedMilliseconds >= _progressIntervalMs)
            {
                await SendProgressAsync(downloadId, totalBytes, downloaded, rate, "Downloading", null, null, ct).ConfigureAwait(false);
                lastProgressSend.Restart();
            }
        }
    }

    private async Task DownloadMultiConnectionAsync(string downloadId, string url, string filePath, long totalBytes, int numSegments, CancellationToken ct)
    {
        await SendProgressAsync(downloadId, totalBytes, 0, 0, "Downloading", null, null, ct).ConfigureAwait(false);

        var segmentSize = (totalBytes + numSegments - 1) / numSegments;
        var ranges = new List<(long Start, long End)>();
        for (var i = 0; i < numSegments; i++)
        {
            var start = i * segmentSize;
            var end = i == numSegments - 1 ? totalBytes - 1 : Math.Min(start + segmentSize - 1, totalBytes - 1);
            if (start <= end)
                ranges.Add((start, end));
        }

        long totalDownloaded = 0;
        var sw = Stopwatch.StartNew();

        var writeLock = new SemaphoreSlim(1, 1);
        try
        {
            await using (var initStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous))
            {
                initStream.SetLength(totalBytes);
            }

            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.Write, 65536, FileOptions.Asynchronous);
            var segmentTasks = ranges.Select(async range =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(range.Start, range.End);
                using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var buffer = new byte[81920];
                long segmentOffset = range.Start;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await writeLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        fileStream.Seek(segmentOffset, SeekOrigin.Begin);
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                    segmentOffset += read;
                    Interlocked.Add(ref totalDownloaded, read);
                }
            });
            await Task.WhenAll(segmentTasks).ConfigureAwait(false);
        }
        finally
        {
        }
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
