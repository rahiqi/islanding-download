using System.Net;
using downloader_agent.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DownloadWorkerOptions>(builder.Configuration.GetSection("DownloadWorker"));
builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection("Heartbeat"));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
// Env overrides so Docker/env vars always win (e.g. KAFKA_BOOTSTRAP_SERVERS, DISPATCHER_URL)
builder.Services.PostConfigure<DownloadWorkerOptions>(options =>
{
    var bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS");
    if (!string.IsNullOrWhiteSpace(bootstrap))
        options.BootstrapServers = bootstrap.Trim();
    var path = Environment.GetEnvironmentVariable("AGENT_DOWNLOAD_PATH");
    if (!string.IsNullOrWhiteSpace(path))
        options.DownloadPath = path.Trim();
});
builder.Services.PostConfigure<HeartbeatOptions>(options =>
{
    var bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS");
    if (!string.IsNullOrWhiteSpace(bootstrap))
        options.BootstrapServers = bootstrap.Trim();
});
builder.Services.PostConfigure<AgentOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.DispatcherUrl))
        options.DispatcherUrl = Environment.GetEnvironmentVariable("DISPATCHER_URL") ?? "";
    if (string.IsNullOrWhiteSpace(options.Name))
        options.Name = Environment.GetEnvironmentVariable("AGENT_NAME") ?? "";
    if (string.IsNullOrWhiteSpace(options.Location))
        options.Location = Environment.GetEnvironmentVariable("AGENT_LOCATION") ?? "";
    if (string.IsNullOrWhiteSpace(options.AgentId))
        options.AgentId = Environment.GetEnvironmentVariable("AGENT_ID") ?? Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];
    if (string.IsNullOrWhiteSpace(options.LocalServeBaseUrl))
        options.LocalServeBaseUrl = (Environment.GetEnvironmentVariable("AGENT_LOCAL_URL") ?? "").TrimEnd('/');
});
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(ChromeDownloadHeaders.HttpClientName, client =>
{
    ChromeDownloadHeaders.Apply(client);
    client.Timeout = TimeSpan.FromHours(2);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All
});
builder.Services.AddSingleton<DownloadWorkerService>();
builder.Services.AddHostedService<AgentRegistrationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DownloadWorkerService>());
builder.Services.AddHostedService<HeartbeatService>();

var app = builder.Build();


app.MapGet("/", () => new
{
    service = "download-agent",
    status = "running"
});
app.MapGet("/health", () => Results.Ok());

app.MapGet("/downloads/{downloadId}", (string downloadId, IOptions<DownloadWorkerOptions> options) =>
{
    if (string.IsNullOrWhiteSpace(downloadId) || downloadId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        return Results.BadRequest();
    var basePath = Path.GetFullPath(options.Value.DownloadPath);
    var path = Path.Combine(basePath, downloadId);
    if (!Path.Exists(path))
        return Results.NotFound();
    string filePath;
    string downloadFileName;
    if (Directory.Exists(path))
    {
        var files = Directory.GetFiles(path);
        if (files.Length == 0)
            return Results.NotFound();
        filePath = files[0];
        downloadFileName = Path.GetFileName(filePath);
    }
    else
    {
        filePath = path;
        downloadFileName = downloadId;
    }
    if (!File.Exists(filePath))
        return Results.NotFound();
    // Use path overload so Kestrel can use sendfile (Linux) / TransmitFile (Windows) for max throughput
    return Results.File(filePath, "application/octet-stream", downloadFileName, enableRangeProcessing: true);
});

app.Run();

static bool IsPrivateOrLoopback(System.Net.IPAddress ip)
{
    if (System.Net.IPAddress.IsLoopback(ip)) return true;
    var bytes = ip.GetAddressBytes();
    if (bytes.Length == 4) // IPv4
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    if (bytes.Length == 16) // IPv6: ::1 and fd00::/8 (ULA)
        return bytes[15] == 1 && bytes.Take(15).All(b => b == 0) || bytes[0] == 0xfd;
    return false;
}
