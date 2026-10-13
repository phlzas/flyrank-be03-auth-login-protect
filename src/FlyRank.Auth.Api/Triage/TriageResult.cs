using System.Text.Json.Serialization;

namespace FlyRank.Auth.Api.Triage;

/// <summary>The validated, schema-shaped answer returned to the caller.</summary>
public sealed record TriageResult(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("urgency")] string Urgency,
    [property: JsonPropertyName("suggested_team")] string SuggestedTeam,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("reason")] string Reason);
