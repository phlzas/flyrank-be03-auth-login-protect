namespace FlyRank.Auth.Api.Llm;

/// <summary>
/// Loads the triage prompt from prompts/triage-v2.md and exposes its version.
/// The prompt is a versioned file, not a string inside a route handler.
/// </summary>
public sealed class PromptLoader
{
    public PromptLoader()
    {
        var path = Path.Combine(RepoPaths.ResolveRepoRoot(), "prompts", "triage-v2.md");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Prompt file not found: {path}");
        }

        System = File.ReadAllText(path);
        Version = Path.GetFileNameWithoutExtension(path);
    }

    public string Version { get; }
    public string System { get; }
}
