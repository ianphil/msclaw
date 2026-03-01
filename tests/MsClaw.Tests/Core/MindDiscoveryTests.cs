using MsClaw.Core;
using MsClaw.Models;
using NSubstitute;
using Xunit;

namespace MsClaw.Tests.Core;

public class MindDiscoveryTests
{
    [Fact]
    public void Discover_ReturnsCachedPath_WhenValid()
    {
        using var home = new TemporaryHomeScope();
        var cachedPath = CreateDirectory("cached");
        var config = Substitute.For<IConfigPersistence>();
        var validator = Substitute.For<IMindValidator>();

        config.Load().Returns(new MsClawConfig { MindRoot = cachedPath });
        validator.Validate(cachedPath).Returns(new MindValidationResult());

        var sut = new MindDiscovery(config, validator);

        var result = sut.Discover();

        Assert.Equal(cachedPath, result);
        validator.Received(1).Validate(cachedPath);
    }

    [Fact]
    public void Discover_SkipsInvalidCachedPath_AndFallsThroughToConvention()
    {
        using var current = new TemporaryCurrentDirectoryScope();
        using var home = new TemporaryHomeScope();

        var cachedPath = CreateDirectory("cached-invalid");
        var conventionPath = current.Path;
        var config = Substitute.For<IConfigPersistence>();
        var validator = Substitute.For<IMindValidator>();

        config.Load().Returns(new MsClawConfig { MindRoot = cachedPath });
        validator.Validate(Arg.Any<string>()).Returns(new MindValidationResult { Errors = ["invalid"] });
        validator.Validate(conventionPath).Returns(new MindValidationResult());

        var sut = new MindDiscovery(config, validator);

        var result = sut.Discover();

        Assert.Equal(conventionPath, result);
        validator.Received(1).Validate(cachedPath);
        validator.Received(1).Validate(conventionPath);
    }

    [Fact]
    public void Discover_ReturnsNull_WhenNothingFound()
    {
        using var current = new TemporaryCurrentDirectoryScope();
        using var home = new TemporaryHomeScope();

        var config = Substitute.For<IConfigPersistence>();
        var validator = Substitute.For<IMindValidator>();
        config.Load().Returns((MsClawConfig?)null);
        validator.Validate(Arg.Any<string>()).Returns(new MindValidationResult { Errors = ["invalid"] });

        var sut = new MindDiscovery(config, validator);

        var result = sut.Discover();

        Assert.Null(result);
    }

    private static string CreateDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TemporaryHomeScope : IDisposable
    {
        private readonly string tempHome = Path.Combine(Path.GetTempPath(), $"msclaw-home-{Guid.NewGuid():N}");
        private readonly string? originalHome = Environment.GetEnvironmentVariable("HOME");
        private readonly string? originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");

        public TemporaryHomeScope()
        {
            Directory.CreateDirectory(tempHome);
            Environment.SetEnvironmentVariable("HOME", tempHome);
            Environment.SetEnvironmentVariable("USERPROFILE", tempHome);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("HOME", originalHome);
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);

            if (Directory.Exists(tempHome))
            {
                Directory.Delete(tempHome, recursive: true);
            }
        }
    }

    private sealed class TemporaryCurrentDirectoryScope : IDisposable
    {
        private readonly string original = Directory.GetCurrentDirectory();

        public string Path { get; }

        public TemporaryCurrentDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"msclaw-cwd-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            Directory.SetCurrentDirectory(Path);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(original);

            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
