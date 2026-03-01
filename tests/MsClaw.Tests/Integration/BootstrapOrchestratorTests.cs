using MsClaw.Core;
using MsClaw.Models;
using MsClaw.Tests.TestHelpers;
using Xunit;

namespace MsClaw.Tests.Integration;

public sealed class BootstrapOrchestratorTests : IDisposable
{
    private readonly string _tempHome;
    private readonly string _configPath;

    public BootstrapOrchestratorTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"msclaw-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempHome);
        _configPath = Path.Combine(_tempHome, ".msclaw", "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }
    }

    [Fact]
    public void Run_NewMind_ScaffoldsValidatesPersistsAndReturnsExpectedFlags()
    {
        using var fixture = new TempMindFixture();
        var newMindPath = Path.Combine(fixture.MindRoot, "new-mind");
        var sut = CreateSut();

        var result = sut.Run(["--new-mind", newMindPath]);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(newMindPath), result!.MindRoot);
        Assert.True(result.IsNewMind);
        Assert.True(result.HasBootstrapMarker);
        Assert.True(File.Exists(Path.Combine(result.MindRoot, "bootstrap.md")));

        var persisted = new ConfigPersistence(_configPath).Load();
        Assert.NotNull(persisted);
        Assert.Equal(result.MindRoot, persisted!.MindRoot);
    }

    [Fact]
    public void Run_ExplicitMind_ValidatesPersistsAndReturnsExistingMindFlags()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateValidMind();
        var sut = CreateSut();

        var result = sut.Run(["--mind", mindRoot]);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(mindRoot), result!.MindRoot);
        Assert.False(result.IsNewMind);
        Assert.False(result.HasBootstrapMarker);

        var persisted = new ConfigPersistence(_configPath).Load();
        Assert.NotNull(persisted);
        Assert.Equal(result.MindRoot, persisted!.MindRoot);
    }

    [Fact]
    public void Run_InvalidMindPath_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var invalidPath = Path.Combine(Path.GetTempPath(), $"missing-mind-{Guid.NewGuid():N}");

        Assert.Throws<InvalidOperationException>(() => sut.Run(["--mind", invalidPath]));
    }

    [Fact]
    public void Run_ResetConfig_ReturnsNullAndClearsConfig()
    {
        var persistence = new ConfigPersistence(_configPath);
        persistence.Save(new MsClawConfig { MindRoot = "/tmp/should-be-cleared" });

        var result = CreateSut().Run(["--reset-config"]);

        Assert.Null(result);
        Assert.Null(persistence.Load());
    }

    [Fact]
    public void Run_UnknownFlag_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();

        Assert.Throws<InvalidOperationException>(() => sut.Run(["--nope"]));
    }

    [Fact]
    public void Run_MindAndNewMindTogether_ThrowsInvalidOperationException()
    {
        using var fixture = new TempMindFixture();
        var existingMind = fixture.CreateValidMind();
        var newMind = Path.Combine(fixture.MindRoot, "new-mind");
        var sut = CreateSut();

        Assert.Throws<InvalidOperationException>(() => sut.Run(["--mind", existingMind, "--new-mind", newMind]));
    }

    private BootstrapOrchestrator CreateSut()
    {
        var validator = new MindValidator();
        var persistence = new ConfigPersistence(_configPath);
        var discovery = new MindDiscovery(persistence, validator);
        var scaffold = new MindScaffold();
        return new BootstrapOrchestrator(validator, discovery, scaffold, persistence);
    }

}
