using System.Text.Json;
using dispatcher.Models;
using dispatcher.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Stores and broadcast
builder.Services.AddSingleton<IDownloadStore, DownloadStore>();
builder.Services.AddSingleton<IAgentStore, AgentStore>();
builder.Services.AddSingleton<IProgressBroadcaster, ProgressBroadcaster>();

// Kafka
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services.Configure<KafkaConsumerOptions>(builder.Configuration.GetSection("KafkaConsumer"));
builder.Services.Configure<AgentHeartbeatConsumerOptions>(builder.Configuration.GetSection("AgentHeartbeat"));
builder.Services.AddSingleton<IKafkaDownloadProducer, KafkaDownloadProducer>();
builder.Services.AddSingleton<IKafkaTopicEnsurer, KafkaTopicEnsurer>();
builder.Services.AddHostedService<ProgressConsumerHostedService>();
builder.Services.AddHostedService<AgentHeartbeatHostedService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddOpenApi();
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.MapOpenApi();

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// --- Downloads API ---
var downloadsApi = app.MapGroup("/api/downloads");

downloadsApi.MapPost("/", async (DownloadSubmitRequest request, IDownloadStore store, IKafkaDownloadProducer producer, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        return Results.BadRequest(new { error = "Invalid or missing URL." });

    var downloadId = Guid.NewGuid().ToString("N");
    var preferredAgentId = string.IsNullOrWhiteSpace(request.PreferredAgentId) ? null : request.PreferredAgentId.Trim();
    var state = new DownloadState(downloadId, request.Url.Trim(), "Queued", DateTime.UtcNow, PreferredAgentId: preferredAgentId);
    store.Add(state);
    await producer.EnqueueAsync(downloadId, state.Url, preferredAgentId, ct);
    return Results.Created($"/api/downloads/{downloadId}", new { downloadId, url = state.Url, status = state.Status });
})
.WithName("SubmitDownload");

downloadsApi.MapGet("/", (IDownloadStore store) =>
{
    var list = store.GetAll();
    return Results.Ok(list);
})
.WithName("ListDownloads");

downloadsApi.MapGet("/{id}", (string id, IDownloadStore store) =>
{
    var d = store.Get(id);
    return d is not null ? Results.Ok(d) : Results.NotFound();
})
.WithName("GetDownload");

// SSE progress stream
downloadsApi.MapGet("/events", async (HttpContext ctx, IProgressBroadcaster broadcaster) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    await foreach (var msg in broadcaster.Subscribe(ctx.RequestAborted))
    {
        var data = JsonSerializer.Serialize(new
        {
            msg.DownloadId,
            msg.AgentId,
            msg.TotalBytes,
            msg.DownloadedBytes,
            msg.BytesPerSecond,
            msg.Status,
            msg.Message,
            msg.LocalDownloadUrl
        }, jsonOptions);
        await ctx.Response.WriteAsync("data: " + data + "\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }
})
.WithName("DownloadProgressStream");

// --- Agents API ---
app.MapGet("/api/agents", (IAgentStore agentStore) =>
{
    var agents = agentStore.GetAvailableAgents(TimeSpan.FromMinutes(2));
    return Results.Ok(agents);
})
.WithName("ListAgents");

app.MapPost("/api/agents/register", async (AgentRegisterRequest? request, IAgentStore agentStore, IKafkaTopicEnsurer topicEnsurer, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.AgentId))
        return Results.BadRequest(new { error = "Request body must contain a non-empty 'agentId'." });
    await topicEnsurer.EnsureAgentTopicsAsync(ct);
    await topicEnsurer.EnsureAgentDownloadQueueTopicAsync(request.AgentId.Trim(), ct);
    agentStore.RegisterAgent(request.AgentId.Trim(), request.Name?.Trim() ?? "", request.Location?.Trim() ?? "");
    return Results.Ok(new { agentId = request.AgentId });
})
.WithName("RegisterAgent");

// Serve SPA static files (React) when not in development proxy
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapFallbackToFile("index.html");

app.Run();
