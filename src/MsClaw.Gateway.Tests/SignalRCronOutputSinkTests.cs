using Microsoft.AspNetCore.SignalR;
using MsClaw.Gateway.Hubs;
using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class SignalRCronOutputSinkTests
{
    [Fact]
    public async Task PublishResultAsync_PushesCronRunEventToAllConnectedClients()
    {
        var client = new RecordingGatewayHubClient();
        var hubContext = new StubHubContext(client);
        var sut = new SignalRCronOutputSink(hubContext);
        var cronRunEvent = new CronRunEvent(
            "job-1",
            "Daily Inbox Check",
            "run-1",
            CronRunOutcome.Success,
            "Inbox summary",
            null,
            125);

        await sut.PublishResultAsync(cronRunEvent, CancellationToken.None);

        var deliveredEvent = Assert.Single(client.CronResults);
        Assert.Equal("job-1", deliveredEvent.JobId);
        Assert.Equal("Daily Inbox Check", deliveredEvent.JobName);
        Assert.Equal("run-1", deliveredEvent.RunId);
        Assert.Equal(CronRunOutcome.Success, deliveredEvent.Outcome);
        Assert.Equal("Inbox summary", deliveredEvent.Content);
        Assert.Null(deliveredEvent.ErrorMessage);
        Assert.Equal(125, deliveredEvent.DurationMs);
    }

    private sealed class RecordingGatewayHubClient : IGatewayHubClient
    {
        public List<CronRunEvent> CronResults { get; } = [];

        public Task ReceiveEvent(GitHub.Copilot.SDK.SessionEvent sessionEvent)
        {
            return Task.CompletedTask;
        }

        public Task ReceivePresence(PresenceSnapshot presence)
        {
            return Task.CompletedTask;
        }

        public Task ReceiveAuthContext(GatewayAuthContext authContext)
        {
            return Task.CompletedTask;
        }

        public Task ReceiveCronResult(CronRunEvent cronRunEvent)
        {
            CronResults.Add(cronRunEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHubContext(IGatewayHubClient client) : IHubContext<GatewayHub, IGatewayHubClient>
    {
        public IHubClients<IGatewayHubClient> Clients { get; } = new StubHubClients(client);

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class StubHubClients(IGatewayHubClient client) : IHubClients<IGatewayHubClient>
    {
        public IGatewayHubClient All => client;

        public IGatewayHubClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => client;

        public IGatewayHubClient Client(string connectionId) => client;

        public IGatewayHubClient Clients(IReadOnlyList<string> connectionIds) => client;

        public IGatewayHubClient Group(string groupName) => client;

        public IGatewayHubClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => client;

        public IGatewayHubClient Groups(IReadOnlyList<string> groupNames) => client;

        public IGatewayHubClient User(string userId) => client;

        public IGatewayHubClient Users(IReadOnlyList<string> userIds) => client;
    }
}
