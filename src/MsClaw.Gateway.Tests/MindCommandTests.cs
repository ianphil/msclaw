using MsClaw.Gateway.Commands.Mind;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class MindCommandTests
{
    [Fact]
    public void ValidateCommand_DefinesPathArgument()
    {
        var command = ValidateCommand.Create();

        Assert.Contains(command.Arguments, argument => argument.Name.Equals("path", StringComparison.Ordinal));
    }

    [Fact]
    public void ScaffoldCommand_DefinesPathArgument()
    {
        var command = ScaffoldCommand.Create();

        Assert.Contains(command.Arguments, argument => argument.Name.Equals("path", StringComparison.Ordinal));
    }
}
