namespace MsClaw.Core;

public sealed class IdentityLoader : IIdentityLoader
{
    public async Task<string> LoadSystemMessageAsync(string mindRoot, CancellationToken cancellationToken = default)
    {
        var soulPath = Path.Combine(mindRoot, "SOUL.md");
        var soulContent = await File.ReadAllTextAsync(soulPath, cancellationToken);

        var agentsPath = Path.Combine(mindRoot, ".github", "agents");
        if (!Directory.Exists(agentsPath))
        {
            return soulContent;
        }

        var agentFiles = Directory.GetFiles(agentsPath, "*.agent.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (agentFiles.Length == 0)
        {
            return soulContent;
        }

        var parts = new List<string> { soulContent };
        foreach (var agentFile in agentFiles)
        {
            var content = await File.ReadAllTextAsync(agentFile, cancellationToken);
            parts.Add(StripFrontmatter(content));
        }

        return string.Join("\n\n---\n\n", parts);
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
