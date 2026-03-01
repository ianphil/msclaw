using Xunit;
using MsClaw.Core;

namespace MsClaw.Tests.Core;

public class MindScaffoldTests
{
    [Fact]
    public void Scaffold_CreatesFullDirectoryStructure()
    {
        var mindRoot = CreateTempDirectory();
        var sut = new MindScaffold();

        sut.Scaffold(mindRoot);

        Assert.True(File.Exists(Path.Combine(mindRoot, "SOUL.md")));
        Assert.True(File.Exists(Path.Combine(mindRoot, "bootstrap.md")));

        Assert.True(Directory.Exists(Path.Combine(mindRoot, ".working-memory")));
        Assert.True(File.Exists(Path.Combine(mindRoot, ".working-memory", "memory.md")));
        Assert.True(File.Exists(Path.Combine(mindRoot, ".working-memory", "rules.md")));
        Assert.True(File.Exists(Path.Combine(mindRoot, ".working-memory", "log.md")));

        Assert.True(Directory.Exists(Path.Combine(mindRoot, ".github", "agents")));
        Assert.True(Directory.Exists(Path.Combine(mindRoot, ".github", "skills")));
        Assert.True(Directory.Exists(Path.Combine(mindRoot, "domains")));
        Assert.True(Directory.Exists(Path.Combine(mindRoot, "initiatives")));
        Assert.True(Directory.Exists(Path.Combine(mindRoot, "expertise")));
        Assert.True(Directory.Exists(Path.Combine(mindRoot, "inbox")));
        Assert.True(Directory.Exists(Path.Combine(mindRoot, "Archive")));
    }

    [Fact]
    public void Scaffold_WritesSoulTemplate_FromEmbeddedResource()
    {
        var mindRoot = CreateTempDirectory();
        var sut = new MindScaffold();

        sut.Scaffold(mindRoot);

        var actual = File.ReadAllText(Path.Combine(mindRoot, "SOUL.md"));
        var expected = ReadTemplate("SOUL.md");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Scaffold_WritesBootstrapTemplate_FromEmbeddedResource()
    {
        var mindRoot = CreateTempDirectory();
        var sut = new MindScaffold();

        sut.Scaffold(mindRoot);

        var actual = File.ReadAllText(Path.Combine(mindRoot, "bootstrap.md"));
        var expected = ReadTemplate("bootstrap.md");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Scaffold_WritesWorkingMemoryFiles_WithExpectedHeaders()
    {
        var mindRoot = CreateTempDirectory();
        var sut = new MindScaffold();

        sut.Scaffold(mindRoot);

        Assert.Equal("# AI Notes — Memory\n", File.ReadAllText(Path.Combine(mindRoot, ".working-memory", "memory.md")));
        Assert.Equal("# AI Notes — Rules\n", File.ReadAllText(Path.Combine(mindRoot, ".working-memory", "rules.md")));
        Assert.Equal("# AI Notes — Log\n", File.ReadAllText(Path.Combine(mindRoot, ".working-memory", "log.md")));
    }

    [Fact]
    public void Scaffold_ExistingNonEmptyDirectory_Throws()
    {
        var mindRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(mindRoot, "existing.txt"), "x");
        var sut = new MindScaffold();

        Assert.Throws<InvalidOperationException>(() => sut.Scaffold(mindRoot));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"msclaw-scaffold-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadTemplate(string fileName)
    {
        var assembly = typeof(CopilotRuntimeClient).Assembly;
        var resourceName = $"MsClaw.Templates.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
