using MsClaw.Models;
using Xunit;

namespace MsClaw.Tests.Models;

public sealed class ChatRequestTests
{
    [Fact]
    public void Message_IsRequired_DefaultsToEmpty()
    {
        var request = new ChatRequest();
        
        Assert.NotNull(request.Message);
        Assert.Equal(string.Empty, request.Message);
    }

    [Fact]
    public void Message_CanBeSet()
    {
        var request = new ChatRequest
        {
            Message = "Hello, agent!"
        };
        
        Assert.Equal("Hello, agent!", request.Message);
    }

    [Fact]
    public void SessionId_IsOptional_DefaultsToNull()
    {
        var request = new ChatRequest();
        
        Assert.Null(request.SessionId);
    }

    [Fact]
    public void SessionId_CanBeSet()
    {
        var request = new ChatRequest
        {
            SessionId = "test-session-id-123"
        };
        
        Assert.Equal("test-session-id-123", request.SessionId);
    }

    [Fact]
    public void SessionId_CanBeSetToNull()
    {
        var request = new ChatRequest
        {
            SessionId = "initial-value"
        };
        
        request.SessionId = null;
        
        Assert.Null(request.SessionId);
    }

    [Fact]
    public void BothProperties_CanBeSetTogether()
    {
        var request = new ChatRequest
        {
            Message = "Test message",
            SessionId = "test-session-id"
        };
        
        Assert.Equal("Test message", request.Message);
        Assert.Equal("test-session-id", request.SessionId);
    }
}
