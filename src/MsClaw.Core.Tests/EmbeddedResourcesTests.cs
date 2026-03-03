using Xunit;

namespace MsClaw.Core.Tests;

public class EmbeddedResourcesTests
{
    [Fact]
    public void ReadTemplate_SoulMd_ReturnsNonEmptyContent()
    {
        var content = EmbeddedResources.ReadTemplate("SOUL.md");

        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public void ReadTemplate_BootstrapMd_ReturnsNonEmptyContent()
    {
        var content = EmbeddedResources.ReadTemplate("bootstrap.md");

        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public void ReadTemplate_NonExistentResource_ThrowsInvalidOperation()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => EmbeddedResources.ReadTemplate("does-not-exist.md"));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadTemplate_SoulMd_ContainsMarkdownHeading()
    {
        var content = EmbeddedResources.ReadTemplate("SOUL.md");

        Assert.StartsWith("#", content.TrimStart());
    }
}
