using System.Security.Claims;
using System.Text.Json;
using dispatcher.Models;
using dispatcher.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Cookie auth (simple username-only login)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "download-portal.auth";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/api/auth/me"; // used for redirect; we use API only
    });

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

// Credentials (cookies) require explicit origins; wildcard (*) is not allowed with credentials.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();
if (corsOrigins.Length == 0)
{
    // Defaults so dev and same-origin work without config
    corsOrigins = new[] { "http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:8084", "http://127.0.0.1:8084", "https://localhost:5173", "https://localhost:8084" };
}
app.UseCors(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials());
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// --- Downloads API ---
var downloadsApi = app.MapGroup("/api/downloads");

downloadsApi.MapPost("/", async (HttpContext ctx, DownloadSubmitRequest request, IDownloadStore store, IKafkaDownloadProducer producer, CancellationToken ct) =>
{
    var username = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;
    if (string.IsNullOrWhiteSpace(username))
        return Results.Json(new { error = "You must be logged in to queue a download." }, statusCode: 401);

    if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        return Results.BadRequest(new { error = "Invalid or missing URL." });

    var downloadId = Guid.NewGuid().ToString("N");
    var preferredAgentId = string.IsNullOrWhiteSpace(request.PreferredAgentId) ? null : request.PreferredAgentId.Trim();
    var state = new DownloadState(downloadId, request.Url.Trim(), "Queued", DateTime.UtcNow, PreferredAgentId: preferredAgentId, QueuedBy: username);
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

downloadsApi.MapGet("/{id}/control", (string id, IDownloadStore store) =>
{
    var action = store.GetRequestedAction(id);
    return Results.Ok(new { action = action ?? "none" });
})
.WithName("GetDownloadControl");

downloadsApi.MapPost("/{id}/pause", (string id, IDownloadStore store) =>
{
    if (store.Get(id) is null) return Results.NotFound();
    store.SetRequestedAction(id, "pause");
    return Results.Ok(new { downloadId = id, action = "pause" });
})
.WithName("PauseDownload");

downloadsApi.MapPost("/{id}/resume", async (string id, IDownloadStore store, IKafkaDownloadProducer producer, CancellationToken ct) =>
{
    var d = store.Get(id);
    if (d is null) return Results.NotFound();
    if (d.Status != "Paused") return Results.BadRequest(new { error = "Download is not paused." });
    if (string.IsNullOrEmpty(d.AgentId)) return Results.BadRequest(new { error = "No agent assigned to resume." });
    store.SetRequestedAction(id, null);
    store.UpdateProgress(id, d.TotalBytes, d.DownloadedBytes, 0, "Queued", d.AgentId, null, d.LocalDownloadUrl);
    await producer.EnqueueResumeAsync(id, d.Url, d.DownloadedBytes, d.AgentId, ct);
    return Results.Ok(new { downloadId = id, action = "resume" });
})
.WithName("ResumeDownload");

downloadsApi.MapPost("/{id}/cancel", (string id, IDownloadStore store) =>
{
    if (store.Get(id) is null) return Results.NotFound();
    store.SetRequestedAction(id, "cancel");
    return Results.Ok(new { downloadId = id, action = "cancel" });
})
.WithName("CancelDownload");

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

// --- Auth API (simple username-only login) ---
app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext ctx) =>
{
    var username = request.Username?.Trim();
    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest(new { error = "Username is required." });
    if (username.Length > 64)
        return Results.BadRequest(new { error = "Username too long." });

    var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
    identity.AddClaim(new Claim(ClaimTypes.Name, username));
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = true });
    return Results.Ok(new { username });
})
.WithName("Login");

app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
})
.WithName("Logout");

app.MapGet("/api/auth/me", (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
        return Results.Json(new { error = "Not logged in" }, statusCode: 401);
    return Results.Ok(new { username = ctx.User.Identity.Name });
})
.WithName("GetCurrentUser");

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
