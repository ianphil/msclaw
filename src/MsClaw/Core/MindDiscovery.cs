namespace MsClaw.Core;

public sealed class MindDiscovery : IMindDiscovery
{
    private readonly IConfigPersistence _configPersistence;
    private readonly IMindValidator _validator;

    public MindDiscovery(IConfigPersistence configPersistence, IMindValidator validator)
    {
        _configPersistence = configPersistence;
        _validator = validator;
    }

    public string? Discover()
    {
        var cachedMindRoot = _configPersistence.Load()?.MindRoot;
        if (IsValidCandidate(cachedMindRoot))
        {
            return cachedMindRoot;
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        if (IsValidCandidate(currentDirectory))
        {
            return currentDirectory;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var msclawMind = Path.Combine(home, ".msclaw", "mind");
        if (IsValidCandidate(msclawMind))
        {
            return msclawMind;
        }

        return null;
    }

    private bool IsValidCandidate(string? mindRoot)
    {
        return !string.IsNullOrWhiteSpace(mindRoot)
            && Directory.Exists(mindRoot)
            && _validator.Validate(mindRoot).IsValid;
    }
}
