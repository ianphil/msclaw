using System.CommandLine;
using Spectre.Console;
using MsClaw.Core;

namespace MsClaw.Gateway.Commands.Mind;

public static class ScaffoldCommand
{
    public static Command Create()
    {
        var command = new Command("scaffold", "Scaffold a mind directory");
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the new mind directory"
        };
        command.Add(pathArgument);
        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument);
            if (string.IsNullOrWhiteSpace(path))
            {
                AnsiConsole.Console.MarkupLine("[red]Mind path is required.[/]");

                return 1;
            }

            return Execute(path, new MindScaffold(), AnsiConsole.Console);
        });

        return command;
    }

    public static int Execute(string path, IMindScaffold mindScaffold, IAnsiConsole console)
    {
        try
        {
            mindScaffold.Scaffold(path);
            console.MarkupLine($"[green]Scaffolded mind at:[/] {Markup.Escape(path)}");

            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Scaffold failed:[/] {Markup.Escape(ex.Message)}");

            return 1;
        }
    }
}
