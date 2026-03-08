namespace MsClaw.Core;

public sealed class IdentityLoader : IIdentityLoader
{
    /// <summary>
    /// Assembles a system message from <c>SOUL.md</c> and any <c>.agent.md</c> files found
    /// under <c>.github/agents/</c>, separated by horizontal rules.
    /// </summary>
    public async Task<string> LoadSystemMessageAsync(string mindRoot, CancellationToken cancellationToken = default)
    {
        var soulPath = Path.Combine(mindRoot, MindPaths.SoulFile);
        var soulContent = await File.ReadAllTextAsync(soulPath, cancellationToken);

        var agentContents = await LoadAgentContentAsync(mindRoot, cancellationToken);

        if (agentContents.Count == 0)
        {
            return soulContent;
        }

        var parts = new List<string>(agentContents.Count + 1) { soulContent };
        parts.AddRange(agentContents);

        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// Reads all <c>.agent.md</c> files from <c>.github/agents/</c>, strips YAML frontmatter,
    /// and returns their contents in deterministic order. Returns an empty list when the
    /// directory is missing or contains no matching files.
    /// </summary>
    private static async Task<List<string>> LoadAgentContentAsync(
        string mindRoot,
        CancellationToken cancellationToken)
    {
        var agentsPath = Path.Combine(mindRoot, MindPaths.GitHubDir, MindPaths.AgentsDir);

        if (!Directory.Exists(agentsPath))
        {
            return [];
        }

        var agentFiles = Directory.GetFiles(agentsPath, MindPaths.AgentFilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (agentFiles.Length == 0)
        {
            return [];
        }

        var results = new List<string>(agentFiles.Length);

        foreach (var agentFile in agentFiles)
        {
            var content = await File.ReadAllTextAsync(agentFile, cancellationToken);
            results.Add(StripFrontmatter(content));
        }

        return results;
    }

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return content;
        }

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        return endIndex > 0
            ? content[(endIndex + 3)..].TrimStart()
            : content;
    }
}
