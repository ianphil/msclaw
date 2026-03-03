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
    }
}
