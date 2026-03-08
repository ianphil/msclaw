namespace MsClaw.Core;

public sealed class MindScaffold : IMindScaffold
{
    private static readonly (string Name, string ResourcePath)[] DefaultSkills =
    [
        ("commit", ".github.skills.commit.SKILL.md"),
        ("capture", ".github.skills.capture.SKILL.md"),
        ("daily-report", ".github.skills.daily_report.SKILL.md"),
    ];

    /// <summary>
    /// Creates the full directory structure and template files for a new mind.
    /// </summary>
    /// <param name="mindRoot">Root directory for the mind. Must not exist or must be empty.</param>
    /// <exception cref="InvalidOperationException">The directory exists and is not empty.</exception>
    public void Scaffold(string mindRoot)
    {
        if (Directory.Exists(mindRoot) && Directory.EnumerateFileSystemEntries(mindRoot).Any())
        {
            throw new InvalidOperationException($"Cannot scaffold into non-empty directory: {mindRoot}");
        }

        Directory.CreateDirectory(mindRoot);

        WriteRootTemplates(mindRoot);
        CreateWorkingMemory(mindRoot);
        CreateGitHubStructure(mindRoot);
        CreateIdeaDirectories(mindRoot);
    }

    private static void WriteRootTemplates(string mindRoot)
    {
        File.WriteAllText(Path.Combine(mindRoot, MindPaths.SoulFile), EmbeddedResources.ReadTemplate(MindPaths.SoulFile));
        File.WriteAllText(Path.Combine(mindRoot, MindPaths.BootstrapFile), EmbeddedResources.ReadTemplate(MindPaths.BootstrapFile));
    }

    private static void CreateWorkingMemory(string mindRoot)
    {
        var workingMemoryPath = Path.Combine(mindRoot, MindPaths.WorkingMemoryDir);
        Directory.CreateDirectory(workingMemoryPath);

        File.WriteAllText(Path.Combine(workingMemoryPath, MindPaths.MemoryFile), "# AI Notes — Memory\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, MindPaths.RulesFile), "# AI Notes — Rules\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, MindPaths.LogFile), "# AI Notes — Log\n");
    }

    private static void CreateGitHubStructure(string mindRoot)
    {
        var githubPath = Path.Combine(mindRoot, MindPaths.GitHubDir);
        Directory.CreateDirectory(githubPath);

        File.WriteAllText(
            Path.Combine(githubPath, MindPaths.CopilotInstructionsFile),
            EmbeddedResources.ReadTemplateByPath(".github.copilot-instructions.md"));

        Directory.CreateDirectory(Path.Combine(githubPath, MindPaths.AgentsDir));

        var skillsPath = Path.Combine(githubPath, MindPaths.SkillsDir);
        Directory.CreateDirectory(skillsPath);

        foreach (var (name, resourcePath) in DefaultSkills)
        {
            WriteSkill(skillsPath, name, resourcePath);
        }
    }

    private static void CreateIdeaDirectories(string mindRoot)
    {
        Directory.CreateDirectory(Path.Combine(mindRoot, MindPaths.DomainsDir));
        Directory.CreateDirectory(Path.Combine(mindRoot, MindPaths.InitiativesDir));
        Directory.CreateDirectory(Path.Combine(mindRoot, MindPaths.ExpertiseDir));
        Directory.CreateDirectory(Path.Combine(mindRoot, MindPaths.InboxDir));
        Directory.CreateDirectory(Path.Combine(mindRoot, MindPaths.ArchiveDir));
    }

    private static void WriteSkill(string skillsPath, string skillName, string resourcePath)
    {
        var skillDir = Path.Combine(skillsPath, skillName);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            EmbeddedResources.ReadTemplateByPath(resourcePath));
    }
}
