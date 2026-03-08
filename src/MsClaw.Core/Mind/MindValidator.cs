namespace MsClaw.Core;

public sealed class MindValidator : IMindValidator
{
    public MindValidationResult Validate(string mindRoot)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var found = new List<string>();

        if (!Directory.Exists(mindRoot))
        {
            errors.Add($"Mind root directory does not exist: {mindRoot}");
            return new MindValidationResult { Errors = errors, Warnings = warnings, Found = found };
        }

        found.Add(mindRoot);
        ValidateRoot(mindRoot, errors, found);
        ValidateWorkingMemory(mindRoot, errors, warnings, found);
        ValidateOptionalStructure(mindRoot, warnings, found);

        return new MindValidationResult { Errors = errors, Warnings = warnings, Found = found };
    }

    /// <summary>Checks that the required SOUL.md file exists in the mind root.</summary>
    private static void ValidateRoot(string mindRoot, List<string> errors, List<string> found)
    {
        var soulPath = Path.Combine(mindRoot, MindPaths.SoulFile);
        if (File.Exists(soulPath))
        {
            found.Add(soulPath);
        }
        else
        {
            errors.Add($"Required file missing: {soulPath}");
        }
    }

    /// <summary>Checks that the .working-memory/ directory and its expected files exist.</summary>
    private static void ValidateWorkingMemory(string mindRoot, List<string> errors, List<string> warnings, List<string> found)
    {
        var workingMemoryPath = Path.Combine(mindRoot, MindPaths.WorkingMemoryDir);
        if (Directory.Exists(workingMemoryPath))
        {
            found.Add(workingMemoryPath);

            CheckOptionalFile(workingMemoryPath, MindPaths.MemoryFile, warnings, found);
            CheckOptionalFile(workingMemoryPath, MindPaths.RulesFile, warnings, found);
            CheckOptionalFile(workingMemoryPath, MindPaths.LogFile, warnings, found);
        }
        else
        {
            errors.Add($"Required directory missing: {workingMemoryPath}");
        }
    }

    /// <summary>Checks optional directories such as agents, skills, domains, and archive.</summary>
    private static void ValidateOptionalStructure(string mindRoot, List<string> warnings, List<string> found)
    {
        CheckOptionalDirectory(mindRoot, MindPaths.GitHubDir, MindPaths.AgentsDir, warnings, found);
        CheckOptionalDirectory(mindRoot, MindPaths.GitHubDir, MindPaths.SkillsDir, warnings, found);
        CheckOptionalDirectory(mindRoot, MindPaths.DomainsDir, null, warnings, found);
        CheckOptionalDirectory(mindRoot, MindPaths.InitiativesDir, null, warnings, found);
        CheckOptionalDirectory(mindRoot, MindPaths.ExpertiseDir, null, warnings, found);
        CheckOptionalDirectory(mindRoot, MindPaths.InboxDir, null, warnings, found);
        CheckOptionalDirectory(mindRoot, MindPaths.ArchiveDir, null, warnings, found);
    }

    private static void CheckOptionalFile(string directory, string fileName, ICollection<string> warnings, ICollection<string> found)
    {
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            found.Add(path);
            return;
        }

        warnings.Add($"Optional file missing: {path}");
    }

    private static void CheckOptionalDirectory(string root, string dirName, string? childDirName, ICollection<string> warnings, ICollection<string> found)
    {
        var path = childDirName is null
            ? Path.Combine(root, dirName)
            : Path.Combine(root, dirName, childDirName);

        if (Directory.Exists(path))
        {
            found.Add(path);
            return;
        }

        warnings.Add($"Optional directory missing: {path}");
    }
}
