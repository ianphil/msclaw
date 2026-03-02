using MsClaw.Core;
using Xunit;

namespace MsClaw.Tests.Core;

public sealed class ICopilotRuntimeClientContractTests
{
    [Fact]
    public void Interface_DoesNotImplementIAsyncDisposable()
    {
        var interfaces = typeof(ICopilotRuntimeClient).GetInterfaces();

        Assert.DoesNotContain(typeof(IAsyncDisposable), interfaces);
    }

    [Fact]
    public void Interface_HasExactlyTwoMethods()
    {
        var methods = typeof(ICopilotRuntimeClient).GetMethods();

        Assert.Equal(2, methods.Length);
        Assert.Contains(methods, m => m.Name == "CreateSessionAsync");
        Assert.Contains(methods, m => m.Name == "SendMessageAsync");
    }
}
