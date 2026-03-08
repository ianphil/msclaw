using System.Text.Json;

namespace MsClaw.Core;

/// <summary>
/// Loads and saves user-level MsClaw configuration from <c>~/.msclaw/config.json</c>.
/// </summary>
public sealed class UserConfigLoader : IUserConfigLoader
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string configPath;

    /// <summary>
    /// Creates a new config loader using the default user config path.
    /// </summary>
    public UserConfigLoader()
        : this(GetDefaultConfigPath())
    {
    }

    /// <summary>
    /// Creates a new config loader pinned to a specific config file path.
    /// </summary>
    public UserConfigLoader(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Config path must be provided.", nameof(configPath));
        }

        this.configPath = Path.GetFullPath(configPath);
    }

    /// <inheritdoc />
    public UserConfig Load()
    {
        if (File.Exists(configPath) is false)
        {
            return new UserConfig();
        }

        var json = File.ReadAllText(configPath);
        try
        {
            return JsonSerializer.Deserialize<UserConfig>(json, JsonReadOptions)
                ?? throw new InvalidOperationException($"User config at '{configPath}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse user config file '{configPath}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void Save(UserConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException($"Unable to determine config directory for '{configPath}'.");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(config, JsonWriteOptions);
        File.WriteAllText(configPath, json);
    }

    /// <inheritdoc />
    public string GetConfigPath()
    {
        return configPath;
    }

    private static string GetDefaultConfigPath()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userHome))
        {
            throw new InvalidOperationException("Unable to resolve user profile directory for MsClaw config.");
        }

        return Path.Combine(userHome, ".msclaw", "config.json");
    }
}
