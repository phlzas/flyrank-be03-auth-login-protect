namespace FlyRank.Auth.Api.Llm;

/// <summary>
/// Locates repo-root folders (prompts/, logs/, evals/) by walking up from the running
/// assembly, so the app finds them no matter which directory it is launched from.
/// </summary>
public static class RepoPaths
{
    public static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FlyRank.Auth.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "prompts")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    public static string LogsDir
    {
        get
        {
            var dir = Path.Combine(ResolveRepoRoot(), "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
