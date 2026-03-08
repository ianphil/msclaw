using System.CommandLine;
using MsClaw.Gateway.Commands.Auth;
using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Commands.Mind;

namespace MsClaw.Gateway;

public static class Program
{
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("MsClaw gateway");
        var authCommand = new Command("auth", "Authentication operations");
        authCommand.Add(LoginCommand.Create());
        var mindCommand = new Command("mind", "Mind operations");
        mindCommand.Add(ValidateCommand.Create());
        mindCommand.Add(ScaffoldCommand.Create());
        rootCommand.Add(StartCommand.Create());
        rootCommand.Add(authCommand);
        rootCommand.Add(mindCommand);

        return rootCommand;
    }

    public static int Main(string[] args)
    {
        var parseResult = CreateRootCommand().Parse(args, new ParserConfiguration());

        return parseResult.Invoke(new InvocationConfiguration());
    }
}
