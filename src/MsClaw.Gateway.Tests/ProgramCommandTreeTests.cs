using Xunit;

namespace MsClaw.Gateway.Tests;

public class ProgramCommandTreeTests
{
    [Fact]
    public void CreateRootCommand_RegistersStartAndMindSubcommands()
    {
        var root = Program.CreateRootCommand();
        var subcommandNames = root.Subcommands.Select(command => command.Name).ToArray();

        Assert.Contains("start", subcommandNames);
        Assert.Contains("mind", subcommandNames);
    }
}
