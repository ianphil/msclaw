namespace MsClaw.Core.Tests;

public sealed class TempMindFixture : IDisposable
{
    public string MindRoot { get; }

    public TempMindFixture()
    {
        MindRoot = Path.Combine(Path.GetTempPath(), $"msclaw-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(MindRoot);
    }

    public string CreateValidMind()
    {
        File.WriteAllText(Path.Combine(MindRoot, "SOUL.md"), "# SOUL");

        var workingMemory = Path.Combine(MindRoot, ".working-memory");
        Directory.CreateDirectory(workingMemory);
        File.WriteAllText(Path.Combine(workingMemory, "memory.md"), "# AI Notes — Memory\n");
        File.WriteAllText(Path.Combine(workingMemory, "rules.md"), "# AI Notes — Rules\n");
        File.WriteAllText(Path.Combine(workingMemory, "log.md"), "# AI Notes — Log\n");

        Directory.CreateDirectory(Path.Combine(MindRoot, "domains"));
        Directory.CreateDirectory(Path.Combine(MindRoot, "initiatives"));
        Directory.CreateDirectory(Path.Combine(MindRoot, "expertise"));
        Directory.CreateDirectory(Path.Combine(MindRoot, "inbox"));
        Directory.CreateDirectory(Path.Combine(MindRoot, "Archive"));

        Directory.CreateDirectory(Path.Combine(MindRoot, ".github", "agents"));
        Directory.CreateDirectory(Path.Combine(MindRoot, ".github", "skills"));

        return MindRoot;
    }

    public string CreateMinimalMind()
    {
        File.WriteAllText(Path.Combine(MindRoot, "SOUL.md"), "# SOUL");
        Directory.CreateDirectory(Path.Combine(MindRoot, ".working-memory"));
        return MindRoot;
    }

    public string CreateEmptyDir()
    {
        return MindRoot;
    }

    public void Dispose()
    {
        if (Directory.Exists(MindRoot))
        {
            Directory.Delete(MindRoot, recursive: true);
        }
    }
}
