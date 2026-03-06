using GitHub.Copilot.SDK;
using MsClaw.OpenResponses.Infrastructure;
using Xunit;

namespace MsClaw.OpenResponses.Tests;

public class OpenResponseEventMapperTests
{
    [Fact]
    public void Map_AssistantMessageDeltaEvent_ReturnsOutputTextDeltaFrame()
    {
        var sessionEvent = new AssistantMessageDeltaEvent
        {
            Data = new AssistantMessageDeltaData
            {
                MessageId = "message-1",
                DeltaContent = "hello"
            }
        };

        var frames = OpenResponseEventMapper.Map("resp_123", sessionEvent);

        Assert.Collection(
            frames,
            frame =>
            {
                Assert.Equal("response.output_text.delta", frame.EventName);
                Assert.Contains("\"delta\":\"hello\"", frame.Data, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Map_AssistantMessageEvent_ReturnsOutputDoneAndCompletedFrames()
    {
        var sessionEvent = new AssistantMessageEvent
        {
            Data = new AssistantMessageData
            {
                MessageId = "message-1",
                Content = "done"
            }
        };

        var frames = OpenResponseEventMapper.Map("resp_123", sessionEvent);

        Assert.Collection(
            frames,
            doneFrame =>
            {
                Assert.Equal("response.output_text.done", doneFrame.EventName);
                Assert.Contains("\"text\":\"done\"", doneFrame.Data, StringComparison.Ordinal);
            },
            completedFrame =>
            {
                Assert.Equal("response.completed", completedFrame.EventName);
                Assert.Contains("\"status\":\"completed\"", completedFrame.Data, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Map_SessionIdleEvent_ReturnsTerminalDoneFrame()
    {
        var sessionEvent = new SessionIdleEvent
        {
            Data = new SessionIdleData()
        };

        var frames = OpenResponseEventMapper.Map("resp_123", sessionEvent);

        Assert.Collection(
            frames,
            frame =>
            {
                Assert.Null(frame.EventName);
                Assert.Equal("[DONE]", frame.Data);
            });
    }

    [Fact]
    public void Map_SessionErrorEvent_ReturnsFailureFrame()
    {
        var sessionEvent = new SessionErrorEvent
        {
            Data = new SessionErrorData
            {
                ErrorType = "runtime_error",
                Message = "boom"
            }
        };

        var frames = OpenResponseEventMapper.Map("resp_123", sessionEvent);

        Assert.Collection(
            frames,
            failedFrame =>
            {
                Assert.Equal("response.failed", failedFrame.EventName);
                Assert.Contains("\"message\":\"boom\"", failedFrame.Data, StringComparison.Ordinal);
            },
            doneFrame =>
            {
                Assert.Equal("[DONE]", doneFrame.Data);
            });
    }
}
