using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CrashCollector.AI;

/// <summary>
/// Unified LLM client supporting OpenAI, Gemini, and custom endpoints.
/// Follows the HTTP-based pattern from the Howler project — no vendor SDKs needed.
/// </summary>
public sealed class LlmClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LlmSettings _settings;

    public LlmClient(HttpClient httpClient, IOptions<LlmSettings> settings)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
        _settings = settings.Value;
    }

    /// <summary>
    /// Send a prompt to the configured default model and return the text response.
    /// </summary>
    public Task<LLMResponse> GenerateContentAsync(string prompt, CancellationToken ct = default)
        => GenerateContentAsync(prompt, preferredModel: null, ct);

    /// <summary>
    /// Send a prompt to a specific (or default) model and return the text response.
    /// </summary>
    public async Task<LLMResponse> GenerateContentAsync(string prompt, string? preferredModel, CancellationToken ct = default)
    {
        // ── resolve model config ──
        var modelName = preferredModel;
        if (string.IsNullOrEmpty(modelName) || !_settings.Models.ContainsKey(modelName))
            modelName = _settings.DefaultModel;
        if (string.IsNullOrEmpty(modelName) || !_settings.Models.ContainsKey(modelName))
            modelName = _settings.Models.Keys.FirstOrDefault();

        if (string.IsNullOrEmpty(modelName) || !_settings.Models.TryGetValue(modelName, out var cfg))
        {
            return new LLMResponse { Success = false, Error = "No valid LLM configuration found." };
        }

        var apiKey = cfg.GetNextApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new LLMResponse { Success = false, Error = $"No API key configured for model '{modelName}'." };
        }

        // ── build provider-specific request ──
        string requestUrl;
        object payload;
        bool useBearer;

        if (cfg.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            var geminiBase = string.IsNullOrEmpty(cfg.Endpoint) || cfg.Endpoint.Contains("api.openai.com")
                ? (_settings.GeminiFallbackEndpoint ?? "https://generativelanguage.googleapis.com")
                : cfg.Endpoint.TrimEnd('/');

            requestUrl = $"{geminiBase}/v1beta/models/{cfg.ModelName}:generateContent?key={apiKey}";
            useBearer = false;

            payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = cfg.Temperature,
                    maxOutputTokens = cfg.MaxTokens
                }
            };
        }
        else // OpenAi, Custom, or anything else → OpenAI-compatible chat/completions
        {
            var endpoint = cfg.Endpoint.TrimEnd('/');
            if (endpoint.EndsWith("/chat/completions"))
                requestUrl = endpoint;
            else if (endpoint.EndsWith("/v1"))
                requestUrl = $"{endpoint}/chat/completions";
            else
                requestUrl = $"{endpoint}/chat/completions";

            useBearer = true;

            payload = new
            {
                model = cfg.ModelName,
                messages = new object[]
                {
                    new { role = "system", content = "You are an expert crash analyst for Windows applications. Analyze crashes and provide root cause analysis and code fixes." },
                    new { role = "user", content = prompt }
                },
                temperature = cfg.Temperature,
                max_tokens = cfg.MaxTokens
            };
        }

        // ── send request ──
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        if (useBearer)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new LLMResponse
                {
                    Success = false,
                    Error = $"LLM API {(int)response.StatusCode}: {responseJson}"
                };
            }

            // ── parse response ──
            using var doc = JsonDocument.Parse(responseJson);
            string? text;
            int promptTokens = 0, responseTokens = 0;

            if (cfg.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
                {
                    if (usage.TryGetProperty("promptTokenCount", out var pt)) promptTokens = pt.GetInt32();
                    if (usage.TryGetProperty("candidatesTokenCount", out var rt)) responseTokens = rt.GetInt32();
                }
            }
            else
            {
                text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (doc.RootElement.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out var rt)) responseTokens = rt.GetInt32();
                }
            }

            // Strip markdown code blocks if the model wrapped its response
            if (text != null)
            {
                if (text.StartsWith("```json")) text = text[7..];
                else if (text.StartsWith("```")) text = text[3..];
                if (text.EndsWith("```")) text = text[..^3];
                text = text.Trim();
            }

            return new LLMResponse
            {
                Success = true,
                Text = text ?? string.Empty,
                PromptTokens = promptTokens,
                ResponseTokens = responseTokens
            };
        }
        catch (TaskCanceledException)
        {
            return new LLMResponse { Success = false, Error = "LLM request timed out." };
        }
        catch (Exception ex)
        {
            return new LLMResponse { Success = false, Error = $"LLM call failed: {ex.Message}" };
        }
    }

    public void Dispose() => _httpClient?.Dispose();
}
