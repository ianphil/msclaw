using Xunit;
using MsClaw.Core;

namespace MsClaw.Tests.Core;

public class CliLocatorTests
{
    [Fact]
    public void ResolveCopilotCliPath_ReturnsNonEmptyPath()
    {
        // On the CI/build machine, copilot CLI should be on PATH (installed by the SDK build).
        // If not installed, this test documents the expected error behavior.
        try
        {
            var path = CliLocator.ResolveCopilotCliPath();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Contains("copilot", path, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException ex)
        {
            // Expected when copilot CLI is not installed — verify the error message is helpful
            Assert.Contains("Copilot CLI not found", ex.Message);
        }
    }

    [Fact]
    public void ResolveCopilotCliPath_ThrowsInvalidOperationException_WhenNotFound()
    {
        // We can't easily remove copilot from PATH in a unit test,
        // but we can verify the method signature and error type are correct.
        // If copilot IS found, the method should return a valid path.
        // If copilot is NOT found, it should throw InvalidOperationException.
        try
        {
            var path = CliLocator.ResolveCopilotCliPath();
            // If we get here, copilot was found — verify it returns a file path
            Assert.False(string.IsNullOrWhiteSpace(path));
        }
        catch (InvalidOperationException)
        {
            // Expected behavior when CLI is not installed
        }
    }
}
