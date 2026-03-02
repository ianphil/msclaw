namespace MsClaw.Core;

public sealed class MindScaffold : IMindScaffold
{
    public void Scaffold(string mindRoot)
    {
        if (Directory.Exists(mindRoot) && Directory.EnumerateFileSystemEntries(mindRoot).Any())
        {
            throw new InvalidOperationException($"Cannot scaffold into non-empty directory: {mindRoot}");
        }

        Directory.CreateDirectory(mindRoot);

        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), EmbeddedResources.ReadTemplate("SOUL.md"));
        File.WriteAllText(Path.Combine(mindRoot, "bootstrap.md"), EmbeddedResources.ReadTemplate("bootstrap.md"));

        var workingMemoryPath = Path.Combine(mindRoot, ".working-memory");
        Directory.CreateDirectory(workingMemoryPath);
        File.WriteAllText(Path.Combine(workingMemoryPath, "memory.md"), "# AI Notes — Memory\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, "rules.md"), "# AI Notes — Rules\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, "log.md"), "# AI Notes — Log\n");

        Directory.CreateDirectory(Path.Combine(mindRoot, ".github", "agents"));
        Directory.CreateDirectory(Path.Combine(mindRoot, ".github", "skills"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "domains"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "initiatives"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "expertise"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "inbox"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "Archive"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "extensions"));
        File.WriteAllText(Path.Combine(mindRoot, "extensions.lock.json"), "{\n  \"extensions\": []\n}\n");
        EnsureMindGitIgnoreIncludesExtensions(mindRoot);
    }

    private static void EnsureMindGitIgnoreIncludesExtensions(string mindRoot)
    {
        var gitIgnorePath = Path.Combine(mindRoot, ".gitignore");
        var entry = "extensions/";

        if (!File.Exists(gitIgnorePath))
        {
            File.WriteAllText(gitIgnorePath, $"{entry}\n");
            return;
        }

        var lines = File.ReadAllLines(gitIgnorePath);
        if (lines.Any(line => line.Trim().Equals(entry, StringComparison.Ordinal)))
        {
            return;
        }

        File.AppendAllText(gitIgnorePath, $"{Environment.NewLine}{entry}{Environment.NewLine}");
    }
}
