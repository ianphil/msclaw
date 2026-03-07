using Xunit;

namespace MsClaw.Tunnel.Tests;

public sealed class DevTunnelLocatorTests
{
    [Fact]
    public void ResolveDevTunnelCliPath_WhenMissing_ThrowsHelpfulError()
    {
        var sut = new DevTunnelLocator();

        try
        {
            var path = sut.ResolveDevTunnelCliPath();
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Contains("devtunnel", path, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException ex)
        {
            Assert.Contains("devtunnel", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PATH", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
