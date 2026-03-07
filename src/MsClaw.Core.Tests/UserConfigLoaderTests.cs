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
}
