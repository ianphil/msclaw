using MsClaw.Core;
using MsClaw.Tests.TestHelpers;
using Xunit;

namespace MsClaw.Tests.Integration;

public sealed class IdentityLoaderIntegrationTests
{
    [Fact]
    public async Task LoadSystemMessageAsync_SoulOnly_ReturnsSoulContent()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateMinimalMind();
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), "# Soul Only");
        var sut = new IdentityLoader();

        var result = await sut.LoadSystemMessageAsync(mindRoot);

        Assert.Equal("# Soul Only", result);
    }

    [Fact]
    public async Task LoadSystemMessageAsync_SoulAndAgent_ComposesContent()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateMinimalMind();
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), "# Soul");
        var agentsDir = Path.Combine(mindRoot, ".github", "agents");
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(Path.Combine(agentsDir, "natalya.agent.md"), "# Agent Instructions");
        var sut = new IdentityLoader();

        var result = await sut.LoadSystemMessageAsync(mindRoot);

        Assert.Equal("# Soul\n\n---\n\n# Agent Instructions", result);
    }

    [Fact]
    public async Task LoadSystemMessageAsync_AgentWithFrontmatter_StripsFrontmatter()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateMinimalMind();
        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), "# Soul");
        var agentsDir = Path.Combine(mindRoot, ".github", "agents");
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(
            Path.Combine(agentsDir, "natalya.agent.md"),
            "---\nname: natalya\ndescription: tester\n---\n\n# Agent Body");
        var sut = new IdentityLoader();

        var result = await sut.LoadSystemMessageAsync(mindRoot);

        Assert.Equal("# Soul\n\n---\n\n# Agent Body", result);
        Assert.DoesNotContain("name:", result);
        Assert.DoesNotContain("description:", result);
    }
}
