using Microsoft.Extensions.Logging.Abstractions;
using MsClaw.Core;
using MsClaw.Gateway.Hosting;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class GatewayHostedServiceTests
{
    [Fact]
    public async Task StartAsync_ValidMind_SetsReadyState()
    {
        var validator = new StubMindValidator(new MindValidationResult());
        var identityLoader = new StubIdentityLoader();
        var fakeClient = new FakeGatewayClient();
        var options = new GatewayOptions { MindPath = "C:\\mind" };
        var sut = new GatewayHostedService(
            validator,
            identityLoader,
            options,
            _ => fakeClient,
            NullLogger<GatewayHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        Assert.Equal(GatewayState.Ready, sut.State);
        Assert.True(sut.IsReady);
        Assert.True(fakeClient.Started);
    }

    [Fact]
    public async Task StartAsync_InvalidMind_SetsFailedState()
    {
        var validator = new StubMindValidator(new MindValidationResult { Errors = ["Missing SOUL.md"] });
        var identityLoader = new StubIdentityLoader();
        var options = new GatewayOptions { MindPath = "C:\\mind" };
        var clientFactoryCalled = false;
        var sut = new GatewayHostedService(
            validator,
            identityLoader,
            options,
            _ =>
            {
                clientFactoryCalled = true;
                return new FakeGatewayClient();
            },
            NullLogger<GatewayHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        Assert.Equal(GatewayState.Failed, sut.State);
        Assert.False(sut.IsReady);
        Assert.Contains("Missing SOUL.md", sut.Error, StringComparison.Ordinal);
        Assert.False(identityLoader.Called);
        Assert.False(clientFactoryCalled);
    }

    [Fact]
    public async Task StartAsync_ValidMind_LoadsIdentity()
    {
        var validator = new StubMindValidator(new MindValidationResult());
        var identityLoader = new StubIdentityLoader();
        var options = new GatewayOptions { MindPath = "C:\\mind" };
        var sut = new GatewayHostedService(
            validator,
            identityLoader,
            options,
            _ => new FakeGatewayClient(),
            NullLogger<GatewayHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        Assert.True(identityLoader.Called);
    }

    [Fact]
    public async Task StopAsync_AfterStart_DisposesClient()
    {
        var validator = new StubMindValidator(new MindValidationResult());
        var identityLoader = new StubIdentityLoader();
        var fakeClient = new FakeGatewayClient();
        var options = new GatewayOptions { MindPath = "C:\\mind" };
        var sut = new GatewayHostedService(
            validator,
            identityLoader,
            options,
            _ => fakeClient,
            NullLogger<GatewayHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        Assert.True(fakeClient.Disposed);
        Assert.Equal(GatewayState.Stopped, sut.State);
    }

    private sealed class StubMindValidator(MindValidationResult result) : IMindValidator
    {
        public MindValidationResult Validate(string mindRoot) => result;
    }

    private sealed class StubIdentityLoader : IIdentityLoader
    {
        public bool Called { get; private set; }

        public Task<string> LoadSystemMessageAsync(string mindRoot, CancellationToken cancellationToken = default)
        {
            Called = true;

            return Task.FromResult("system message");
        }
    }

    private sealed class FakeGatewayClient : IGatewayClient
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
