using System.Text.Json;

namespace FlyRank.Auth.Api.Triage;

public sealed record QuarantineEntry(
    string Ts,
    string PromptVersion,
    string Model,
    string Error,
    string Input,
    string RawOutput,
    string? RawOutputAfterRepair);

/// <summary>
/// Appends answers that failed validation (after the single repair) to
/// logs/quarantine.jsonl, with the input, the error and the prompt version.
/// Bad output is set aside, never returned and never silently replaced.
/// </summary>
public sealed class QuarantineLogger
{
    private static readonly object Gate = new();
    private readonly ILogger<QuarantineLogger> _logger;
    private readonly string _filePath;

    public QuarantineLogger(ILogger<QuarantineLogger> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(Llm.RepoPaths.LogsDir, "quarantine.jsonl");
    }

    public void Log(QuarantineEntry entry)
    {
        _logger.LogWarning(
            "llm.quarantine version={PromptVersion} model={Model} error={Error}",
            entry.PromptVersion, entry.Model, entry.Error);

        lock (Gate)
        {
            File.AppendAllText(_filePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
    }
}
