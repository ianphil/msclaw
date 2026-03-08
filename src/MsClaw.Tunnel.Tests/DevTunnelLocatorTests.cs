using Xunit;

namespace MsClaw.Tunnel.Tests;

public sealed class DevTunnelLocatorTests
{
    [Fact]
    public void ResolveDevTunnelCliPath_SingleExePath_ReturnsExePath()
    {
        var runner = new FakeCommandRunner(exitCode: 0, output: @"C:\tools\devtunnel.exe");
        var sut = new DevTunnelLocator(runner);

        var path = sut.ResolveDevTunnelCliPath();

        Assert.Equal(@"C:\tools\devtunnel.exe", path);
    }

    [Fact]
    public void ResolveDevTunnelCliPath_MultipleCandidates_PrefersExeOverCmd()
    {
        var runner = new FakeCommandRunner(exitCode: 0, output: "C:\\tools\\devtunnel.cmd\r\nC:\\tools\\devtunnel.exe");
        var sut = new DevTunnelLocator(runner);

        var path = sut.ResolveDevTunnelCliPath();

        Assert.Equal(@"C:\tools\devtunnel.exe", path);
    }

    [Fact]
    public void ResolveDevTunnelCliPath_OnlyCmdCandidate_ReturnsCmdPath()
    {
        var runner = new FakeCommandRunner(exitCode: 0, output: @"C:\tools\devtunnel.cmd");
        var sut = new DevTunnelLocator(runner);

        var path = sut.ResolveDevTunnelCliPath();

        Assert.Equal(@"C:\tools\devtunnel.cmd", path);
    }

    [Fact]
    public void ResolveDevTunnelCliPath_NotFound_ThrowsWithGuidance()
    {
        var runner = new FakeCommandRunner(exitCode: 1, output: "");
        var sut = new DevTunnelLocator(runner);

        var ex = Assert.Throws<InvalidOperationException>(() => sut.ResolveDevTunnelCliPath());

        Assert.Contains("devtunnel", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PATH", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDevTunnelCliPath_EmptyOutput_ThrowsWithGuidance()
    {
        var runner = new FakeCommandRunner(exitCode: 0, output: "   ");
        var sut = new DevTunnelLocator(runner);

        var ex = Assert.Throws<InvalidOperationException>(() => sut.ResolveDevTunnelCliPath());

        Assert.Contains("PATH", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeCommandRunner(int exitCode, string output) : ICommandRunner
    {
        public CommandRunnerResult Run(string fileName, string arguments) =>
            new(exitCode, output);
    }
}
