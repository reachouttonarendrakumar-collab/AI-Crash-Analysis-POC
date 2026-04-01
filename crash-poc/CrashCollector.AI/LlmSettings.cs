namespace CrashCollector.AI;

/// <summary>
/// Configuration for LLM providers, modeled after Howler's proven pattern.
/// Supports multiple providers with API key rotation.
/// </summary>
public class LlmSettings
{
    public string DefaultModel { get; set; } = "gpt-4o";
    public string GeminiFallbackEndpoint { get; set; } = "https://generativelanguage.googleapis.com";
    public Dictionary<string, ModelConfig> Models { get; set; } = new();
}

public class ModelConfig
{
    private int _apiKeyIndex = -1;

    public string ModelName { get; set; } = "";
    public string Provider { get; set; } = "OpenAi";
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public List<string> ApiKeys { get; set; } = new();
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Round-robin API key rotation (thread-safe), copied from Howler.
    /// </summary>
    public string GetNextApiKey()
    {
        var allKeys = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApiKey))
            allKeys.Add(ApiKey);
        if (ApiKeys != null)
            allKeys.AddRange(ApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)));

        if (allKeys.Count == 0)
            return "";

        var index = Interlocked.Increment(ref _apiKeyIndex);
        var safeIndex = (int)(Math.Abs((long)index) % allKeys.Count);
        return allKeys[safeIndex];
    }
}
