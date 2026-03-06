using System.Text.Json;
using MsClaw.OpenResponses.Models;
using Xunit;

namespace MsClaw.OpenResponses.Tests;

public class ResponseRequestTests
{
    [Fact]
    public void Deserialize_ValidJson_PopulatesModelInputAndStream()
    {
        var json = """
            {
              "model": "gpt-5",
              "input": "hello",
              "stream": true,
              "user": "caller-1"
            }
            """;

        var request = JsonSerializer.Deserialize<ResponseRequest>(json, ResponseRequest.SerializerOptions);

        Assert.NotNull(request);
        Assert.Equal("gpt-5", request.Model);
        Assert.Equal(JsonValueKind.String, request.Input.ValueKind);
        Assert.Equal("hello", request.Input.GetString());
        Assert.True(request.Stream);
        Assert.Equal("caller-1", request.User);
    }

    [Fact]
    public void Validate_MissingModel_ReturnsError()
    {
        var request = new ResponseRequest
        {
            Input = JsonDocument.Parse("\"hello\"").RootElement.Clone()
        };

        var error = request.Validate();

        Assert.Equal("The 'model' field is required.", error);
    }

    [Fact]
    public void Validate_EmptyInput_ReturnsError()
    {
        var request = new ResponseRequest
        {
            Model = "gpt-5",
            Input = JsonDocument.Parse("\"   \"").RootElement.Clone()
        };

        var error = request.Validate();

        Assert.Equal("The 'input' field must be a non-empty string or non-empty array.", error);
    }
}
