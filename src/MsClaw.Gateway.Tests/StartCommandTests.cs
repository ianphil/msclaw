using MsClaw.Gateway.Commands;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandTests
{
    [Fact]
    public void Create_DefinesMindAndNewMindOptions()
    {
        var command = StartCommand.Create();
        var optionNames = command.Options.Select(option => option.Name).ToArray();

        Assert.Contains("--mind", optionNames);
        Assert.Contains("--new-mind", optionNames);
    }
}
