using System.CommandLine;
using Spectre.Console;
using MsClaw.Core;

namespace MsClaw.Gateway.Commands.Mind;

public static class ValidateCommand
{
    public static Command Create()
    {
        var command = new Command("validate", "Validate a mind directory");
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the mind directory"
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

            return Execute(path, new MindValidator(), AnsiConsole.Console);
        });

        return command;
    }

    public static int Execute(string path, IMindValidator mindValidator, IAnsiConsole console)
    {
        var result = mindValidator.Validate(path);
        var root = new Tree($"[bold]Mind[/] {Markup.Escape(path)}");
        var errors = root.AddNode($"[red]Errors ({result.Errors.Count})[/]");
        foreach (var error in result.Errors)
        {
            errors.AddNode(Markup.Escape(error));
        }

        var warnings = root.AddNode($"[yellow]Warnings ({result.Warnings.Count})[/]");
        foreach (var warning in result.Warnings)
        {
            warnings.AddNode(Markup.Escape(warning));
        }

        var found = root.AddNode($"[green]Found ({result.Found.Count})[/]");
        foreach (var item in result.Found)
        {
            found.AddNode(Markup.Escape(item));
        }

        console.Write(root);

        return result.IsValid ? 0 : 1;
    }
}
