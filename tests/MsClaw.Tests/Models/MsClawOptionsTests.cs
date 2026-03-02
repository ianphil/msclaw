using MsClaw.Models;
using Xunit;

namespace MsClaw.Tests.Models;

public sealed class MsClawOptionsTests
{
    [Fact]
    public void MindRoot_DefaultsToEmpty()
    {
        var options = new MsClawOptions();
        
        Assert.Equal(string.Empty, options.MindRoot);
    }

    [Fact]
    public void Port_DefaultsTo5000()
    {
        var options = new MsClawOptions();
        
        Assert.Equal(5000, options.Port);
    }

    [Fact]
    public void AutoGitPull_DefaultsToFalse()
    {
        var options = new MsClawOptions();
        
        Assert.False(options.AutoGitPull);
    }

    [Fact]
    public void AgentName_DefaultsToMsClaw()
    {
        var options = new MsClawOptions();
        
        Assert.Equal("msclaw", options.AgentName);
    }

    [Fact]
    public void Model_DefaultsToClaudeSonnet45()
    {
        var options = new MsClawOptions();
        
        Assert.Equal("claude-sonnet-4.5", options.Model);
    }

    [Fact]
    public void AllProperties_CanBeSet()
    {
        var options = new MsClawOptions
        {
            MindRoot = "/test/path",
            Port = 8080,
            AutoGitPull = true,
            AgentName = "test-agent",
            Model = "gpt-5.3-codex"
        };
        
        Assert.Equal("/test/path", options.MindRoot);
        Assert.Equal(8080, options.Port);
        Assert.True(options.AutoGitPull);
        Assert.Equal("test-agent", options.AgentName);
        Assert.Equal("gpt-5.3-codex", options.Model);
    }

    // NOTE: SessionStore property has been removed as part of session management refactor.
    // The SDK now owns session persistence via InfiniteSessionConfig, so the custom
    // file-based SessionStore is no longer needed. This test documents the removal.
    [Fact]
    public void SessionStore_PropertyDoesNotExist()
    {
        var options = new MsClawOptions();
        var type = options.GetType();
        var property = type.GetProperty("SessionStore");
        
        Assert.Null(property);
    }
}
