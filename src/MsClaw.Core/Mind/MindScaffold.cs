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

        // Root templates
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), EmbeddedResources.ReadTemplate("SOUL.md"));
        File.WriteAllText(Path.Combine(mindRoot, "bootstrap.md"), EmbeddedResources.ReadTemplate("bootstrap.md"));

        // Working memory
        var workingMemoryPath = Path.Combine(mindRoot, ".working-memory");
        Directory.CreateDirectory(workingMemoryPath);
        File.WriteAllText(Path.Combine(workingMemoryPath, "memory.md"), "# AI Notes — Memory\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, "rules.md"), "# AI Notes — Rules\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, "log.md"), "# AI Notes — Log\n");

        // Copilot instructions (bootstrap trigger)
        var githubPath = Path.Combine(mindRoot, ".github");
        Directory.CreateDirectory(githubPath);
        File.WriteAllText(
            Path.Combine(githubPath, "copilot-instructions.md"),
            EmbeddedResources.ReadTemplateByResourceName(".github.copilot-instructions.md"));

        // Agent and skill directories
        Directory.CreateDirectory(Path.Combine(githubPath, "agents"));
        var skillsPath = Path.Combine(githubPath, "skills");
        Directory.CreateDirectory(skillsPath);

        // Embedded skills
        WriteSkill(skillsPath, "commit", ".github.skills.commit.SKILL.md");
        WriteSkill(skillsPath, "capture", ".github.skills.capture.SKILL.md");
        WriteSkill(skillsPath, "daily-report", ".github.skills.daily_report.SKILL.md");

        // IDEA directories
        Directory.CreateDirectory(Path.Combine(mindRoot, "domains"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "initiatives"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "expertise"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "inbox"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "Archive"));
    }

    private static void WriteSkill(string skillsPath, string skillName, string resourceSuffix)
    {
        var skillDir = Path.Combine(skillsPath, skillName);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            EmbeddedResources.ReadTemplateByResourceName(resourceSuffix));
    }
}
