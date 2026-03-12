namespace dispatcher.Models;

public record DownloadSubmitRequest(string? Url, string? PreferredAgentId);

public record AgentRegisterRequest(string AgentId, string? Name, string? Location);

public record LoginRequest(string? Username);
