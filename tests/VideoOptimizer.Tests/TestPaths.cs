namespace VideoOptimizer.Tests;

internal static class TestPaths
{
    public static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "VideoOptimizer.slnx")))
                return directory.FullName;
        throw new InvalidOperationException("Run tests from the repository checkout.");
    }

    public static string PresetsDirectory() => Path.Combine(RepoRoot(), "presets");
    public static string ToolsRoot() => RepoRoot();
}
