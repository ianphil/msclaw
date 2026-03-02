using MsClaw.Models;

namespace MsClaw.Core;

public sealed class BootstrapOrchestrator : IBootstrapOrchestrator
{
    private const string Usage = "Usage: msclaw [--reset-config] [--mind <path> | --new-mind <path>]";

    private readonly IMindValidator _mindValidator;
    private readonly IMindDiscovery _mindDiscovery;
    private readonly IMindScaffold _mindScaffold;
    private readonly IConfigPersistence _configPersistence;

    public BootstrapOrchestrator(
        IMindValidator mindValidator,
        IMindDiscovery mindDiscovery,
        IMindScaffold mindScaffold,
        IConfigPersistence configPersistence)
    {
        _mindValidator = mindValidator;
        _mindDiscovery = mindDiscovery;
        _mindScaffold = mindScaffold;
        _configPersistence = configPersistence;
    }

    public BootstrapResult? Run(string[] args)
    {
        if (args.Contains("--reset-config", StringComparer.Ordinal))
        {
            _configPersistence.Clear();
            Console.Out.WriteLine("Config cleared.");
            return null;
        }

        string? explicitMindPath = null;
        string? newMindPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mind":
                    explicitMindPath = ReadPathValue(args, ref i, "--mind", explicitMindPath);
                    break;
                case "--new-mind":
                    newMindPath = ReadPathValue(args, ref i, "--new-mind", newMindPath);
                    break;
                default:
                    // Skip unknown args — they may be ASP.NET Core host flags (--urls, --environment, etc.)
                    break;
            }
        }

        if (explicitMindPath is not null && newMindPath is not null)
        {
            ThrowUsage("--mind and --new-mind cannot be used together.");
        }

        if (newMindPath is not null)
        {
            var resolvedPath = Path.GetFullPath(newMindPath);
            _mindScaffold.Scaffold(resolvedPath);
            ValidateOrThrow(resolvedPath);
            Persist(resolvedPath);

            return new BootstrapResult
            {
                MindRoot = resolvedPath,
                IsNewMind = true,
                HasBootstrapMarker = true
            };
        }

        if (explicitMindPath is not null)
        {
            var resolvedPath = Path.GetFullPath(explicitMindPath);
            ValidateOrThrow(resolvedPath);
            Persist(resolvedPath);

            return new BootstrapResult
            {
                MindRoot = resolvedPath,
                IsNewMind = false,
                HasBootstrapMarker = File.Exists(Path.Combine(resolvedPath, "bootstrap.md"))
            };
        }

        var discoveredPath = _mindDiscovery.Discover();
        if (string.IsNullOrWhiteSpace(discoveredPath))
        {
            var message = $"No valid mind found.{Environment.NewLine}{Usage}";
            Console.Error.WriteLine(message);
            throw new InvalidOperationException(message);
        }

        var resolvedDiscoveredPath = Path.GetFullPath(discoveredPath);
        ValidateOrThrow(resolvedDiscoveredPath);
        Persist(resolvedDiscoveredPath);

        return new BootstrapResult
        {
            MindRoot = resolvedDiscoveredPath,
            IsNewMind = false,
            HasBootstrapMarker = File.Exists(Path.Combine(resolvedDiscoveredPath, "bootstrap.md"))
        };
    }

    private static string ReadPathValue(string[] args, ref int index, string optionName, string? existingValue)
    {
        if (existingValue is not null)
        {
            ThrowUsage($"Duplicate argument: {optionName}");
        }

        if (index + 1 >= args.Length)
        {
            ThrowUsage($"Missing path for {optionName}.");
        }

        index++;
        return args[index];
    }

    private void ValidateOrThrow(string mindRoot)
    {
        var validationResult = _mindValidator.Validate(mindRoot);
        if (validationResult.IsValid)
        {
            return;
        }

        var details = string.Join(Environment.NewLine, validationResult.Errors.Select(error => $"- {error}"));
        var message = $"Mind validation failed:{Environment.NewLine}{details}";
        Console.Error.WriteLine(message);
        throw new InvalidOperationException(message);
    }

    private void Persist(string resolvedPath)
    {
        _configPersistence.Save(new MsClawConfig
        {
            MindRoot = resolvedPath
        });
    }

    private static void ThrowUsage(string message)
    {
        var fullMessage = $"{message}{Environment.NewLine}{Usage}";
        Console.Error.WriteLine(fullMessage);
        throw new InvalidOperationException(fullMessage);
    }
}
