using Microsoft.Extensions.AI;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class ToolCatalogStoreTests
{
    [Fact]
    public void Add_StoredDescriptor_CanBeRetrievedByName()
    {
        var sut = new ToolCatalogStore();
        var descriptor = new ToolDescriptor
        {
            Function = CreateFunction("tool_a", "Tool A"),
            ProviderName = "provider-a",
            Tier = ToolSourceTier.Bundled
        };

        sut.Add(descriptor, ToolStatus.Ready);

        var result = sut.TryGet("tool_a");

        Assert.Same(descriptor, result);
    }

    private static AIFunction CreateFunction(string name, string description)
    {
        return AIFunctionFactory.Create(
            (string input) => input,
            name,
            description);
    }
}
