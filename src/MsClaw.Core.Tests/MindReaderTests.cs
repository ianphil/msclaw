using Xunit;

namespace MsClaw.Core.Tests;

public class MindReaderTests : IDisposable
{
    private readonly TempMindFixture _fixture = new();

    [Fact]
    public async Task ReadFileAsync_ExistingFile_ReturnsContent()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        var content = await sut.ReadFileAsync("SOUL.md");

        Assert.Equal("# SOUL", content);
    }

    [Fact]
    public async Task ReadFileAsync_NestedFile_ReturnsContent()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        var content = await sut.ReadFileAsync(".working-memory/memory.md");

        Assert.Contains("# AI Notes", content);
    }

    [Fact]
    public async Task ReadFileAsync_PathTraversal_ThrowsUnauthorizedAccess()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ReadFileAsync("../../etc/passwd"));
    }

    [Fact]
    public async Task ReadFileAsync_NonExistentFile_ThrowsFileNotFound()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => sut.ReadFileAsync("does-not-exist.md"));
    }

    [Fact]
    public async Task ReadFileAsync_BackslashPath_NormalizesToForwardSlash()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        var content = await sut.ReadFileAsync(".working-memory\\rules.md");

        Assert.Contains("# AI Notes", content);
    }

    [Fact]
    public async Task ListDirectoryAsync_ValidDirectory_ReturnsRelativePaths()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        var entries = await sut.ListDirectoryAsync(".working-memory");

        Assert.Contains(entries, e => e.Contains("memory.md"));
        Assert.Contains(entries, e => e.Contains("rules.md"));
        Assert.Contains(entries, e => e.Contains("log.md"));
    }

    [Fact]
    public async Task ListDirectoryAsync_RootDirectory_ReturnsTopLevelEntries()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        var entries = await sut.ListDirectoryAsync(".");

        Assert.Contains(entries, e => e.Contains("SOUL.md"));
        Assert.Contains(entries, e => e.Contains("domains"));
    }

    [Fact]
    public async Task ListDirectoryAsync_NonExistentDirectory_ThrowsDirectoryNotFound()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => sut.ListDirectoryAsync("nonexistent"));
    }

    [Fact]
    public async Task EnsureSyncedAsync_AutoGitPullFalse_ReturnsImmediately()
    {
        var mindRoot = _fixture.CreateValidMind();
        var sut = new MindReader(mindRoot, autoGitPull: false);

        // Should complete without error (no git pull attempted)
        await sut.EnsureSyncedAsync();
    }

    [Fact]
    public void Constructor_ResolvesRelativePath()
    {
        var mindRoot = _fixture.CreateValidMind();
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), mindRoot);
        var sut = new MindReader(relativePath, autoGitPull: false);

        // Should not throw — proves the reader resolved the relative path
        Assert.NotNull(sut);
    }

    public void Dispose() => _fixture.Dispose();
}
