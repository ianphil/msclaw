using Xunit;
using MsClaw.Core;
using MsClaw.Tests.TestHelpers;

namespace MsClaw.Tests.Core;

public class MindValidatorTests
{
    [Fact]
    public void Validate_ValidMind_ReturnsValidWithNoErrors()
    {
        using var fixture = new TempMindFixture();
        var sut = new MindValidator();

        var result = sut.Validate(fixture.CreateValidMind());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MissingSoulFile_ReturnsError()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateMinimalMind();
        File.Delete(Path.Combine(mindRoot, "SOUL.md"));
        var sut = new MindValidator();

        var result = sut.Validate(mindRoot);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SOUL.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingWorkingMemoryDirectory_ReturnsError()
    {
        using var fixture = new TempMindFixture();
        var mindRoot = fixture.CreateMinimalMind();
        Directory.Delete(Path.Combine(mindRoot, ".working-memory"), recursive: true);
        var sut = new MindValidator();

        var result = sut.Validate(mindRoot);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(".working-memory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingOptionalDirectories_ReturnsWarningsOnly()
    {
        using var fixture = new TempMindFixture();
        var sut = new MindValidator();

        var result = sut.Validate(fixture.CreateMinimalMind());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("domains", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Contains("agents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EmptyDirectory_ReturnsRequiredErrors()
    {
        using var fixture = new TempMindFixture();
        var sut = new MindValidator();

        var result = sut.Validate(fixture.CreateEmptyDir());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SOUL.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains(".working-memory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FoundList_IncludesExistingEntries()
    {
        using var fixture = new TempMindFixture();
        var sut = new MindValidator();

        var result = sut.Validate(fixture.CreateValidMind());

        Assert.Contains(result.Found, item => item.Contains("SOUL.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains(".working-memory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("memory.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("rules.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("log.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("domains", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("initiatives", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("expertise", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("inbox", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("archive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("extensions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("agents", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Found, item => item.Contains("skills", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NonExistentDirectory_ReturnsError()
    {
        var sut = new MindValidator();
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-mind-{Guid.NewGuid():N}");

        var result = sut.Validate(missingPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }
}
