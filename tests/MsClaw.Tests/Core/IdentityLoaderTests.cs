using Xunit;
using MsClaw.Core;

namespace MsClaw.Tests.Core;

public class IdentityLoaderTests
{
    [Fact]
    public async Task LoadSystemMessageAsync_SoulOnly_ReturnsSoulContent()
    {
        var mindRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), "soul-content");
        var sut = new IdentityLoader();

        var result = await sut.LoadSystemMessageAsync(mindRoot);

        Assert.Equal("soul-content", result);
    }

    [Fact]
    public async Task LoadSystemMessageAsync_SoulAndAgent_ComposesWithSeparator()
    {
        var mindRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), "soul-content");
        var agentsDir = Path.Combine(mindRoot, ".github", "agents");
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(Path.Combine(agentsDir, "tester.agent.md"), "# Agent Instructions");
        var sut = new IdentityLoader();

        var result = await sut.LoadSystemMessageAsync(mindRoot);

        Assert.Contains("soul-content", result);
        Assert.Contains("\n\n---\n\n", result);
        Assert.Contains("# Agent Instructions", result);
    }

    [Fact]
    public async Task LoadSystemMessageAsync_AgentFrontmatter_IsStripped()
    {
        var mindRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), "soul-content");
        var agentsDir = Path.Combine(mindRoot, ".github", "agents");
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(
            Path.Combine(agentsDir, "tester.agent.md"),
            "---\nname: tester\ndescription: test\n---\n\n# Body");

        var sut = new IdentityLoader();

        var result = await sut.LoadSystemMessageAsync(mindRoot);

        Assert.DoesNotContain("name:", result);
        Assert.DoesNotContain("description:", result);
        Assert.Contains("# Body", result);
    }

    [Fact]
    public async Task LoadSystemMessageAsync_MultipleAgentFiles_AllIncluded()
    {
        var mindRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), "soul-content");
        var agentsDir = Path.Combine(mindRoot, ".github", "agents");
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(Path.Combine(agentsDir, "a.agent.md"), "# Agent A");
        File.WriteAllText(Path.Combine(agentsDir, "b.agent.md"), "# Agent B");
        var sut = new IdentityLoader();

        var result = await sut.LoadSystemMessageAsync(mindRoot);

        Assert.Contains("# Agent A", result);
        Assert.Contains("# Agent B", result);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"msclaw-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
