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
builder.Services.AddHostedService<ProgressConsumerHostedService>();
builder.Services.AddHostedService<AgentHeartbeatHostedService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
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
    var state = new DownloadState(downloadId, request.Url.Trim(), "Queued", DateTime.UtcNow);
    store.Add(state);
    await producer.EnqueueAsync(downloadId, state.Url, ct);
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
            msg.Message
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

// Serve SPA static files (React) when not in development proxy
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapFallbackToFile("index.html");

app.Run();

public record DownloadSubmitRequest(string? Url);
