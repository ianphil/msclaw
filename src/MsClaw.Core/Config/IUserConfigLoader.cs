namespace MsClaw.Core;

/// <summary>
/// Provides access to user-level MsClaw configuration stored in the user profile.
/// </summary>
public interface IUserConfigLoader
{
    /// <summary>
    /// Loads user configuration from disk, returning defaults when the config file does not exist.
    /// </summary>
    UserConfig Load();

    /// <summary>
    /// Saves the provided user configuration to disk.
    /// </summary>
    void Save(UserConfig config);

    /// <summary>
    /// Gets the full path of the user configuration file.
    /// </summary>
    string GetConfigPath();
}
