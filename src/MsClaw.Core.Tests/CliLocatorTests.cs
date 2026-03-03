using Xunit;

namespace MsClaw.Core.Tests;

public class CliLocatorTests
{
    [Fact]
    public void ResolveCliPath_KnownBinary_ReturnsNonEmptyPath()
    {
        var binaryName = OperatingSystem.IsWindows() ? "cmd" : "bash";
        var path = CliLocator.ResolveCliPath(binaryName);

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.Contains(binaryName, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveCliPath_NonExistentBinary_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CliLocator.ResolveCliPath("nonexistent-binary-that-does-not-exist-12345"));

        Assert.Contains("not found on PATH", ex.Message);
    }

    [Fact]
    public void ResolveCopilotCliPath_WhenCopilotNotOnPath_ThrowsWithHelpfulMessage()
    {
        try
        {
            var path = CliLocator.ResolveCopilotCliPath();
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Contains("copilot", path, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException ex)
        {
            Assert.Contains("copilot", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PATH", ex.Message);
        }
    }
}
