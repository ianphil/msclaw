using GitHub.Copilot.SDK;
using Xunit;

namespace MsClaw.Core.Tests;

public class MsClawClientFactoryTests
{
    [Fact]
    public void Create_ReturnsNonNullClient()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateMinimalMind();

        var client = MsClawClientFactory.Create(mindRoot);

        Assert.NotNull(client);
    }

    [Fact]
    public void Create_WithConfigure_AppliesOverrides()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateMinimalMind();
        var customCliPath = "/custom/path/copilot";

        var client = MsClawClientFactory.Create(mindRoot, opts =>
        {
            opts.CliPath = customCliPath;
        });

        Assert.NotNull(client);
    }
}
