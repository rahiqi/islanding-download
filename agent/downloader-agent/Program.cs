using downloader_agent.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DownloadWorkerOptions>(builder.Configuration.GetSection("DownloadWorker"));
builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection("Heartbeat"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DownloadWorkerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DownloadWorkerService>());
builder.Services.AddHostedService<HeartbeatService>();

var app = builder.Build();

app.MapGet("/", () => new
{
    service = "download-agent",
    status = "running"
});
app.MapGet("/health", () => Results.Ok());

app.Run();
