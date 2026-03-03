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
            return new MindValidationResult
            {
                Errors = errors,
                Warnings = warnings,
                Found = found
            };
        }

        found.Add(mindRoot);

        var soulPath = Path.Combine(mindRoot, "SOUL.md");
        if (File.Exists(soulPath))
        {
            found.Add(soulPath);
        }
        else
        {
            errors.Add($"Required file missing: {soulPath}");
        }

        var workingMemoryPath = Path.Combine(mindRoot, ".working-memory");
        if (Directory.Exists(workingMemoryPath))
        {
            found.Add(workingMemoryPath);

            CheckOptionalFile(workingMemoryPath, "memory.md", warnings, found);
            CheckOptionalFile(workingMemoryPath, "rules.md", warnings, found);
            CheckOptionalFile(workingMemoryPath, "log.md", warnings, found);
        }
        else
        {
            errors.Add($"Required directory missing: {workingMemoryPath}");
        }

        CheckOptionalDirectory(mindRoot, ".github", "agents", warnings, found);
        CheckOptionalDirectory(mindRoot, ".github", "skills", warnings, found);
        CheckOptionalDirectory(mindRoot, "domains", warnings, found);
        CheckOptionalDirectory(mindRoot, "initiatives", warnings, found);
        CheckOptionalDirectory(mindRoot, "expertise", warnings, found);
        CheckOptionalDirectory(mindRoot, "inbox", warnings, found);
        CheckOptionalDirectory(mindRoot, "Archive", warnings, found);

        return new MindValidationResult
        {
            Errors = errors,
            Warnings = warnings,
            Found = found
        };
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

    private static void CheckOptionalDirectory(string root, string dirName, ICollection<string> warnings, ICollection<string> found)
        => CheckOptionalDirectory(root, dirName, null, warnings, found);

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
