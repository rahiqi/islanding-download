using downloader_agent.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DownloadWorkerOptions>(builder.Configuration.GetSection("DownloadWorker"));
builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection("Heartbeat"));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
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
});
builder.Services.AddHttpClient();
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

app.Run();
