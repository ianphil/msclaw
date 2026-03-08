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

    /// <summary>
    /// Gets or sets the cached authentication session established by <c>msclaw auth login</c>.
    /// </summary>
    [JsonPropertyName("auth")]
    public UserAuthConfig? Auth { get; set; }
}

/// <summary>
/// Represents user-level authentication session metadata and token material.
/// </summary>
public sealed class UserAuthConfig
{
    /// <summary>
    /// Gets or sets the tenant identifier used for token issuance.
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the Entra application/client identifier used for login.
    /// </summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the signed-in account username/email.
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the bearer access token used for gateway-authenticated requests.
    /// </summary>
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the UTC expiration instant of <see cref="AccessToken"/>.
    /// </summary>
    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
