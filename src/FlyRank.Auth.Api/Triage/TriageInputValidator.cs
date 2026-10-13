using System.Text.Json;

namespace FlyRank.Auth.Api.Triage;

public sealed record ValidationResult(bool Ok, string? Field, string? Error);

/// <summary>
/// Validates the request body before any model call, so every rejected request is a
/// model call we did not pay for. The raw <see cref="JsonElement"/> is inspected so a
/// wrong type (not just a missing field) can be reported against the offending field.
/// </summary>
public sealed class TriageInputValidator
{
    public ValidationResult Validate(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return new ValidationResult(false, "text", "Request body must be a JSON object with a 'text' field.");
        }

        if (!body.TryGetProperty("text", out var textElement))
        {
            return new ValidationResult(false, "text", "'text' is required.");
        }

        if (textElement.ValueKind != JsonValueKind.String)
        {
            return new ValidationResult(false, "text", "'text' must be a string.");
        }

        var text = textElement.GetString() ?? string.Empty;
        if (text.Length < TriageSchema.MinTextLength)
        {
            return new ValidationResult(false, "text", "'text' must not be empty.");
        }

        if (text.Length > TriageSchema.MaxTextLength)
        {
            return new ValidationResult(false, "text", $"'text' must be at most {TriageSchema.MaxTextLength} characters (got {text.Length}).");
        }

        return new ValidationResult(true, null, null);
    }
}
