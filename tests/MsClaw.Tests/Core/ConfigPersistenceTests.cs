using MsClaw.Core;
using MsClaw.Models;
using Xunit;

namespace MsClaw.Tests.Core;

public class ConfigPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ConfigPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"msclaw-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, ".msclaw", "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsConfiguration()
    {
        var sut = new ConfigPersistence(_configPath);
        var expected = new MsClawConfig { MindRoot = "/tmp/test-mind" };

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal(expected.MindRoot, loaded!.MindRoot);
    }

    [Fact]
    public void Load_NoFile_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);

        var loaded = sut.Load();

        Assert.Null(loaded);
    }

    [Fact]
    public void Clear_RemovesConfigFile()
    {
        var sut = new ConfigPersistence(_configPath);
        sut.Save(new MsClawConfig { MindRoot = "/tmp/test-mind" });

        sut.Clear();

        Assert.False(File.Exists(_configPath));
    }

    [Fact]
    public void Save_CreatesConfigDirectoryIfNeeded()
    {
        var sut = new ConfigPersistence(_configPath);

        sut.Save(new MsClawConfig { MindRoot = "/tmp/test-mind" });

        Assert.True(Directory.Exists(Path.GetDirectoryName(_configPath)));
    }

    [Fact]
    public void Save_SetsLastUsedTimestamp()
    {
        var sut = new ConfigPersistence(_configPath);
        var config = new MsClawConfig { MindRoot = "/tmp/test-mind", LastUsed = null };

        sut.Save(config);

        Assert.NotNull(config.LastUsed);
        Assert.True(config.LastUsed <= DateTime.UtcNow.AddSeconds(1));
    }
}
