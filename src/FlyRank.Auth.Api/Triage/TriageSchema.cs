namespace FlyRank.Auth.Api.Triage;

/// <summary>The closed lists and limits from JOB-CARD.md, in one place.</summary>
public static class TriageSchema
{
    public const int MinTextLength = 1;
    public const int MaxTextLength = 2000;

    public static readonly string[] Categories = ["billing", "bug", "feature", "other"];
    public static readonly string[] Urgencies = ["low", "normal", "high"];
    public static readonly string[] Teams = ["billing", "engineering", "product", "success", "triage"];
}
