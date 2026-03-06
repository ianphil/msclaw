using System.Text.Json;
using MsClaw.OpenResponses.Models;
using Xunit;

namespace MsClaw.OpenResponses.Tests;

public class ResponseObjectTests
{
    [Fact]
    public void Serialize_ResponseObject_UsesOpenResponsesShape()
    {
        var response = new ResponseObject
        {
            Id = "resp_123",
            Status = "completed",
            Output =
            [
                new OutputItem
                {
                    Content =
                    [
                        new ContentPart
                        {
                            Text = "2+2 equals 4."
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(response, ResponseRequest.SerializerOptions);

        Assert.Contains("\"object\":\"response\"", json, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"resp_123\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"output\":[", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_OutputItemAndContentPart_UsesExpectedDefaults()
    {
        var outputItem = new OutputItem
        {
            Content =
            [
                new ContentPart
                {
                    Text = "hello"
                }
            ]
        };

        var json = JsonSerializer.Serialize(outputItem, ResponseRequest.SerializerOptions);

        Assert.Contains("\"type\":\"message\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"assistant\"", json, StringComparison.Ordinal);
        Assert.Contains("\"content\":[{\"type\":\"output_text\",\"text\":\"hello\"}]", json, StringComparison.Ordinal);
    }
}
