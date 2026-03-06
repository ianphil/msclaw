using System.Text.Json;
using MsClaw.OpenResponses.Models;
using Xunit;

namespace MsClaw.OpenResponses.Tests;

public class ErrorResponseTests
{
    [Fact]
    public void Serialize_ErrorResponse_IncludesCodeMessageAndRequestId()
    {
        var response = new ErrorResponse
        {
            Error = new ErrorDetail
            {
                Code = "conflict",
                Message = "Caller already has an active run.",
                RequestId = "req_123"
            }
        };

        var json = JsonSerializer.Serialize(response, ResponseRequest.SerializerOptions);

        Assert.Contains("\"code\":\"conflict\"", json, StringComparison.Ordinal);
        Assert.Contains("\"message\":\"Caller already has an active run.\"", json, StringComparison.Ordinal);
        Assert.Contains("\"request_id\":\"req_123\"", json, StringComparison.Ordinal);
    }
}
