using MsClaw.Core;
using MsClaw.Gateway.Commands;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandHandlerTests
{
    [Fact]
    public async Task ExecuteStartAsync_NewMind_ScaffoldsBeforeRunningGateway()
    {
        var calls = new List<string>();
        var scaffold = new StubMindScaffold(() => calls.Add("scaffold"));

        var exitCode = await StartCommand.ExecuteStartAsync(
            null,
            "C:\\mind",
            (options, cancellationToken) =>
            {
                calls.Add("run");

                return Task.FromResult(0);
            },
            scaffold,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["scaffold", "run"], calls);
    }

    private sealed class StubMindScaffold(Action onScaffold) : IMindScaffold
    {
        public void Scaffold(string mindRoot)
        {
            onScaffold();
        }
    }
}
