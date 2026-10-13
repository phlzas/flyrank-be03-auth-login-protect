using System.Globalization;

namespace FlyRank.Auth.Api.Llm;

/// <summary>
/// LLM provider settings, read once from environment variables at startup.
/// The three "swap" values are BaseUrl, ApiKey and Model - changing only those
/// moves the same code between LM Studio, Ollama, OpenRouter or OpenAI.
/// </summary>
public sealed class LlmOptions
{
    public string BaseUrl { get; init; } = "http://localhost:1234/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public double Temperature { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxRetries { get; init; } = 3;
    public bool Enabled { get; init; } = true;
    public bool Stub { get; init; }
    public bool JsonMode { get; init; }

    public static LlmOptions FromEnvironment()
    {
        static string? Raw(string name) => Environment.GetEnvironmentVariable(name);

        static bool Bool(string name, bool fallback)
        {
            var raw = Raw(name);
            return string.IsNullOrWhiteSpace(raw)
                ? fallback
                : raw.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
        }

        static int Int(string name, int fallback)
            => int.TryParse(Raw(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        static double Double(string name, double fallback)
            => double.TryParse(Raw(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        return new LlmOptions
        {
            BaseUrl = Raw("LLM_BASE_URL") ?? "http://localhost:1234/v1",
            ApiKey = Raw("LLM_API_KEY") ?? string.Empty,
            Model = Raw("LLM_MODEL") ?? string.Empty,
            Temperature = Double("LLM_TEMPERATURE", 0),
            // An explicit endpoint timeout, clamped to <= 60s. The official SDKs default
            // to ten minutes, which is not a usable timeout for an HTTP request.
            TimeoutSeconds = Math.Clamp(Int("LLM_TIMEOUT_SECONDS", 30), 1, 60),
            MaxRetries = Math.Clamp(Int("LLM_MAX_RETRIES", 3), 0, 5),
            Enabled = Bool("LLM_ENABLED", true),
            Stub = Bool("LLM_STUB", false),
            JsonMode = Bool("LLM_JSON_MODE", false),
        };
    }
}
