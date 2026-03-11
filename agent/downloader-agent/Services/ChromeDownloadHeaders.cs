namespace downloader_agent.Services;

/// <summary>Headers that mimic a Chrome browser request so download URLs that block non-browser clients accept the request.</summary>
internal static class ChromeDownloadHeaders
{
    public const string HttpClientName = "Download";

    /// <summary>Current Chrome on Windows User-Agent (update periodically if sites block old versions).</summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static void Apply(HttpClient client)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-User", "?1");
    }
}
