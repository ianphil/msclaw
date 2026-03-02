using Xunit;
using MsClaw.Core;

namespace MsClaw.Tests.Core;

public class CliLocatorTests
{
    [Fact]
    public void ResolveCliPath_KnownBinary_ReturnsNonEmptyPath()
    {
        // 'bash' is always available on Linux CI runners
        var path = CliLocator.ResolveCliPath("bash");

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.Contains("bash", path, StringComparison.OrdinalIgnoreCase);
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
        // On CI, copilot may or may not be on PATH. If found, verify path;
        // if not found, verify helpful error message.
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
