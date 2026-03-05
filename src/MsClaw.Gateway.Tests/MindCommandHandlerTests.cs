using Spectre.Console;
using MsClaw.Core;
using MsClaw.Gateway.Commands.Mind;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class MindCommandHandlerTests
{
    [Fact]
    public void ValidateCommand_ValidMind_ReturnsZero()
    {
        var validator = new StubMindValidator(new MindValidationResult());
        var console = CreateConsole();

        var exitCode = ValidateCommand.Execute("C:\\mind", validator, console);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateCommand_InvalidMind_ReturnsOne()
    {
        var validator = new StubMindValidator(new MindValidationResult { Errors = ["SOUL.md missing"] });
        var console = CreateConsole();

        var exitCode = ValidateCommand.Execute("C:\\mind", validator, console);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void ScaffoldCommand_CallsScaffold()
    {
        var scaffold = new StubMindScaffold();
        var console = CreateConsole();

        var exitCode = ScaffoldCommand.Execute("C:\\mind", scaffold, console);

        Assert.Equal(0, exitCode);
        Assert.Equal("C:\\mind", scaffold.LastPath);
    }

    private sealed class StubMindValidator(MindValidationResult result) : IMindValidator
    {
        public MindValidationResult Validate(string mindRoot) => result;
    }

    private sealed class StubMindScaffold : IMindScaffold
    {
        public string? LastPath { get; private set; }

        public void Scaffold(string mindRoot)
        {
            LastPath = mindRoot;
        }
    }

    private static IAnsiConsole CreateConsole()
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null)
        });
    }
}
