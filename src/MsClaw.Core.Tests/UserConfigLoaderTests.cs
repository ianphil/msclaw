using System.Text.Json;
using Xunit;

namespace MsClaw.Core.Tests;

public sealed class UserConfigLoaderTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaultConfig()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        var sut = new UserConfigLoader(tempPath);

        var config = sut.Load();

        Assert.Null(config.TunnelId);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTunnelId()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempPath = Path.Combine(tempRoot, "config.json");
        var sut = new UserConfigLoader(tempPath);
        var expected = new UserConfig { TunnelId = "my-tunnel-id" };

        sut.Save(expected);
        var actual = sut.Load();

        Assert.Equal("my-tunnel-id", actual.TunnelId);
        Assert.True(File.Exists(tempPath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAuthConfig()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempPath = Path.Combine(tempRoot, "config.json");
        var sut = new UserConfigLoader(tempPath);
        var expected = new UserConfig
        {
            Auth = new UserAuthConfig
            {
                TenantId = "test-tenant",
                ClientId = "test-client",
                Username = "user@example.com",
                AccessToken = "token-value",
                ExpiresAtUtc = new DateTimeOffset(2026, 01, 02, 03, 04, 05, TimeSpan.Zero)
            }
        };

        sut.Save(expected);
        var actual = sut.Load();

        Assert.NotNull(actual.Auth);
        Assert.Equal("test-tenant", actual.Auth.TenantId);
        Assert.Equal("test-client", actual.Auth.ClientId);
        Assert.Equal("user@example.com", actual.Auth.Username);
        Assert.Equal("token-value", actual.Auth.AccessToken);
        Assert.Equal(expected.Auth.ExpiresAtUtc, actual.Auth.ExpiresAtUtc);
    }

    [Fact]
    public void Load_InvalidJson_ThrowsInvalidOperationException()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempPath = Path.Combine(tempRoot, "config.json");
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(tempPath, "{ invalid-json");
        var sut = new UserConfigLoader(tempPath);

        var ex = Assert.Throws<InvalidOperationException>(() => sut.Load());

        Assert.Contains("Failed to parse user config file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_WritesCamelCasePropertyName()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempPath = Path.Combine(tempRoot, "config.json");
        var sut = new UserConfigLoader(tempPath);

        sut.Save(new UserConfig { TunnelId = "abc123" });
        var json = File.ReadAllText(tempPath);
        var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("tunnelId", out _));
    }

    [Fact]
    public void Save_Auth_WritesCamelCasePropertyNames()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempPath = Path.Combine(tempRoot, "config.json");
        var sut = new UserConfigLoader(tempPath);

        sut.Save(new UserConfig
        {
            Auth = new UserAuthConfig
            {
                TenantId = "tenant",
                ClientId = "client",
                Username = "user@example.com",
                AccessToken = "token",
                ExpiresAtUtc = new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero)
            }
        });

        var json = File.ReadAllText(tempPath);
        var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("auth", out var auth));
        Assert.True(auth.TryGetProperty("tenantId", out _));
        Assert.True(auth.TryGetProperty("clientId", out _));
        Assert.True(auth.TryGetProperty("username", out _));
        Assert.True(auth.TryGetProperty("accessToken", out _));
        Assert.True(auth.TryGetProperty("expiresAtUtc", out _));
    }
}
