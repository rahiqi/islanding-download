using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace downloader_agent.Services;

public sealed class AgentRegistrationService : BackgroundService
{
    private readonly AgentOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgentRegistrationService> _logger;

    public AgentRegistrationService(
        IOptions<AgentOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AgentRegistrationService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DispatcherUrl))
        {
            _logger.LogWarning("DispatcherUrl is not set; skipping agent registration.");
            return;
        }

        var baseUrl = _options.DispatcherUrl.TrimEnd('/');
        var registerUrl = $"{baseUrl}/api/agents/register";
        var body = JsonSerializer.Serialize(new
        {
            agentId = _options.AgentId,
            name = _options.Name ?? "",
            location = _options.Location ?? ""
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.PostAsync(registerUrl, content, stoppingToken);
            
            var responseContent = await response.Content.ReadAsStringAsync(stoppingToken);
            _logger.LogInformation("Dispatcher registration response: {ResponseContent}", responseContent);

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Registered with dispatcher at {DispatcherUrl} as {AgentId} ({Name}, {Location})",
                _options.DispatcherUrl, _options.AgentId, _options.Name, _options.Location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register with dispatcher at {Url}", registerUrl);
        }
    }
}
