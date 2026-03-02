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
        var expected = new MsClawConfig
        {
            MindRoot = "/tmp/test-mind",
            DisabledExtensions = ["ext.one", "ext.two"]
        };

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal(expected.MindRoot, loaded!.MindRoot);
        Assert.Equal(expected.DisabledExtensions, loaded.DisabledExtensions);
    }

    [Fact]
    public void Load_NoFile_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);

        var loaded = sut.Load();

        Assert.Null(loaded);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, "{ definitely-not-json");

        var loaded = sut.Load();

        Assert.Null(loaded);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, string.Empty);

        var loaded = sut.Load();

        Assert.Null(loaded);
    }

    [Fact]
    public void Load_PartialJson_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, "{\"LastUsed\":");

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

    [Fact]
    public void Load_CorruptedJsonWithNullBytes_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllBytes(_configPath, [0x7B, 0x00, 0x22, 0x4D, 0x69, 0x6E, 0x64, 0x52, 0x6F, 0x6F, 0x74, 0x22, 0x3A, 0x22, 0x2F, 0x74, 0x6D, 0x70, 0x22, 0x7D]); // {"MindRoot":"/tmp"} with null byte

        var loaded = sut.Load();

        Assert.Null(loaded);
    }

    [Fact]
    public void Load_JsonWithInvalidUtf8_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllBytes(_configPath, [0xFF, 0xFE, 0x7B, 0x22, 0x4D, 0x69, 0x6E, 0x64, 0x52, 0x6F, 0x6F, 0x74, 0x22, 0x3A, 0x22, 0x2F, 0x74, 0x6D, 0x70, 0x22, 0x7D]); // BOM + JSON

        var loaded = sut.Load();

        Assert.Null(loaded);
    }

    [Fact]
    public void Load_TruncatedJsonObject_ReturnsNull()
    {
        var sut = new ConfigPersistence(_configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, "{\"MindRoot\":\"/tmp\",\"LastUsed\":\"2026-03-01T");

        var loaded = sut.Load();

        Assert.Null(loaded);
    }
}
