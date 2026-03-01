using System.Text.Json;
using MsClaw.Models;

namespace MsClaw.Core;

public sealed class ConfigPersistence : IConfigPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;

    public ConfigPersistence()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".msclaw",
            "config.json"))
    {
    }

    public ConfigPersistence(string configPath)
    {
        _configPath = configPath;
    }

    public MsClawConfig? Load()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<MsClawConfig>(json, JsonOptions);
    }

    public void Save(MsClawConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var directoryPath = Path.GetDirectoryName(_configPath)
            ?? throw new InvalidOperationException($"Could not resolve config directory for path: {_configPath}");

        Directory.CreateDirectory(directoryPath);

        config.LastUsed = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public void Clear()
    {
        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }
}
