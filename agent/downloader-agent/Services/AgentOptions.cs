namespace downloader_agent.Services;

public class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Dispatcher base URL (e.g. http://dispatcher:8080) for registration.</summary>
    public string DispatcherUrl { get; set; } = "";

    /// <summary>Display name for this agent node.</summary>
    public string Name { get; set; } = "";

    /// <summary>Location or datacenter for this agent node.</summary>
    public string Location { get; set; } = "";

    /// <summary>Optional stable agent id; if empty, one is generated at startup.</summary>
    public string? AgentId { get; set; }
}
