using System.Text.Json;
using FlyRank.Auth.Api.Llm;

namespace FlyRank.Auth.Api.Triage;

/// <summary>Outcome of a triage request: either a validated value or a clean error.</summary>
public sealed record TriageOutcome(bool Ok, TriageResult? Value, int StatusCode, string? Error, bool Repaired);

/// <summary>
/// Orchestrates one triage request: validate -> (kill switch / stub) -> call the model
/// -> parse/validate -> repair once -> return clean JSON or a 422. Raw model text is
/// never returned to the caller, on success or on failure.
/// </summary>
public sealed class TriageService
{
    private readonly LlmOptions _options;
    private readonly ILlmClient _llm;
    private readonly PromptLoader _prompt;
    private readonly TriageOutputParser _parser;
    private readonly QuarantineLogger _quarantine;
    private readonly ILogger<TriageService> _logger;

    public TriageService(
        LlmOptions options,
        ILlmClient llm,
        PromptLoader prompt,
        TriageOutputParser parser,
        QuarantineLogger quarantine,
        ILogger<TriageService> logger)
    {
        _options = options;
        _llm = llm;
        _prompt = prompt;
        _parser = parser;
        _quarantine = quarantine;
        _logger = logger;
    }

    public async Task<TriageOutcome> TriageAsync(string text, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("LLM disabled via kill switch (LLM_ENABLED=false); returning deterministic fallback.");
            return new TriageOutcome(true, new TriageResult("other", "low", "triage", 0.0, "Automated triage is temporarily disabled."), StatusCodes.Status200OK, null, false);
        }

        if (_options.Stub)
        {
            return new TriageOutcome(true, new TriageResult("bug", "normal", "engineering", 0.9, "Stub response (LLM_STUB=1) - no model was called."), StatusCodes.Status200OK, null, false);
        }

        // Untrusted content is JSON-encoded and sent as its own user message, so it
        // cannot break out of its quotes or blur into the system instructions.
        var userContent = JsonSerializer.Serialize(new Dictionary<string, string> { ["text"] = text });

        try
        {
            var first = await _llm.CompleteAsync(_prompt.System, userContent, _prompt.Version, cancellationToken);
            var parsed = _parser.Parse(first.Text);
            var repaired = false;

            if (!parsed.Ok)
            {
                repaired = true;
                var repairContent = BuildRepairMessage(userContent, first.Text, parsed.Error!);
                var second = await _llm.CompleteAsync(_prompt.System, repairContent, _prompt.Version, cancellationToken);
                parsed = _parser.Parse(second.Text);

                if (!parsed.Ok)
                {
                    _quarantine.Log(new QuarantineEntry(
                        Ts: DateTimeOffset.UtcNow.ToString("o"),
                        PromptVersion: _prompt.Version,
                        Model: _options.Model,
                        Error: parsed.Error!,
                        Input: text,
                        RawOutput: first.Text,
                        RawOutputAfterRepair: second.Text));

                    return new TriageOutcome(false, null, StatusCodes.Status422UnprocessableEntity, $"The model's answer could not be validated after one repair: {parsed.Error}", repaired);
                }
            }

            return new TriageOutcome(true, parsed.Value, StatusCodes.Status200OK, null, repaired);
        }
        catch (LlmException ex)
        {
            return MapFailure(ex);
        }
    }

    private static TriageOutcome MapFailure(LlmException ex) => ex.Kind switch
    {
        LlmFailureKind.Timeout => new TriageOutcome(false, null, StatusCodes.Status504GatewayTimeout, "The model took too long to respond. Please try again.", false),
        LlmFailureKind.Configuration => new TriageOutcome(false, null, StatusCodes.Status503ServiceUnavailable, "Triage is not configured. Check LLM_BASE_URL / LLM_API_KEY / LLM_MODEL.", false),
        LlmFailureKind.RateLimited => new TriageOutcome(false, null, StatusCodes.Status503ServiceUnavailable, "The model provider is rate-limiting requests. Please try again shortly.", false),
        _ => new TriageOutcome(false, null, StatusCodes.Status502BadGateway, "The model provider failed. Please try again.", false),
    };

    private static string BuildRepairMessage(string userContent, string previousOutput, string error)
        => $"{userContent}\n\nYour previous answer was:\n{previousOutput}\n\nIt was rejected for this reason: {error}.\nReturn only corrected JSON matching the schema.";
}
