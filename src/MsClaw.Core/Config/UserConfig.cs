using System.Text.Json.Serialization;

namespace MsClaw.Core;

/// <summary>
/// Represents user-level MsClaw configuration persisted outside mind directories.
/// </summary>
public sealed class UserConfig
{
    /// <summary>
    /// Gets or sets the persistent dev tunnel identifier used for remote gateway access.
    /// </summary>
    [JsonPropertyName("tunnelId")]
    public string? TunnelId { get; set; }
}
