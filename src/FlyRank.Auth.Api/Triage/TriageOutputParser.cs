using System.Text.Json;

namespace FlyRank.Auth.Api.Triage;

public sealed record ParseResult(bool Ok, TriageResult? Value, string? Error);

/// <summary>
/// Turns raw model text into a validated <see cref="TriageResult"/>, or a precise error.
/// Models wrap JSON in code fences or add prose; the fence is stripped, the object is
/// found, and every field is checked against the closed lists from the job card.
/// </summary>
public sealed class TriageOutputParser
{
    private static readonly string[] AllowedFields = ["category", "urgency", "suggested_team", "confidence", "reason"];

    public ParseResult Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("model returned an empty response");
        }

        var candidate = ExtractJsonObject(raw);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(candidate);
        }
        catch (JsonException ex)
        {
            return Fail($"not valid JSON ({ex.Message})");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("JSON root is not an object");
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!AllowedFields.Contains(property.Name))
                {
                    return Fail($"unexpected field '{property.Name}' (only {string.Join(", ", AllowedFields)} are allowed)");
                }
            }

            if (!TryGetString(root, "category", out var category))
            {
                return Fail("field 'category' is missing or not a string");
            }

            if (!TriageSchema.Categories.Contains(category))
            {
                return Fail($"category '{category}' is not one of [{string.Join(" | ", TriageSchema.Categories)}]");
            }

            if (!TryGetString(root, "urgency", out var urgency))
            {
                return Fail("field 'urgency' is missing or not a string");
            }

            if (!TriageSchema.Urgencies.Contains(urgency))
            {
                return Fail($"urgency '{urgency}' is not one of [{string.Join(" | ", TriageSchema.Urgencies)}]");
            }

            if (!TryGetString(root, "suggested_team", out var team))
            {
                return Fail("field 'suggested_team' is missing or not a string");
            }

            if (!TriageSchema.Teams.Contains(team))
            {
                return Fail($"suggested_team '{team}' is not one of [{string.Join(" | ", TriageSchema.Teams)}]");
            }

            if (!root.TryGetProperty("confidence", out var confidenceElement) || confidenceElement.ValueKind != JsonValueKind.Number)
            {
                return Fail("field 'confidence' is missing or not a number");
            }

            var confidence = confidenceElement.GetDouble();
            if (confidence is < 0 or > 1)
            {
                return Fail($"confidence {confidence} is outside 0.0-1.0");
            }

            if (!TryGetString(root, "reason", out var reason) || reason.Length == 0)
            {
                return Fail("field 'reason' is missing, not a string, or empty");
            }

            if (reason.Length > 300)
            {
                return Fail($"field 'reason' is too long ({reason.Length} > 300 characters)");
            }

            return new ParseResult(true, new TriageResult(category, urgency, team, confidence, reason), null);
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static ParseResult Fail(string message) => new(false, null, message);

    // Strips a ```json fence and any surrounding prose, returning the outermost {...}.
    private static string ExtractJsonObject(string raw)
    {
        var text = raw.Trim();
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text[firstBrace..(lastBrace + 1)];
        }

        return text;
    }
}
