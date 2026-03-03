using GitHub.Copilot.SDK;
using MsClaw.Core;
using Xunit;

namespace MsClaw.Integration.Tests;

public class MindLifecycleTests : IAsyncDisposable
{
    private readonly string _mindRoot;
    private CopilotClient? _client;

    public MindLifecycleTests()
    {
        _mindRoot = Path.Combine(Path.GetTempPath(), $"msclaw-integ-{Guid.NewGuid():N}");
        new MindScaffold().Scaffold(_mindRoot);
    }

    [Fact]
    public void ScaffoldAndValidate_NewMind_IsValid()
    {
        var validator = new MindValidator();
        var result = validator.Validate(_mindRoot);

        Assert.True(result.IsValid, $"Validation errors: {string.Join(", ", result.Errors)}");
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CreateClientAndSendMessage_ReceivesResponse()
    {
        _client = MsClawClientFactory.Create(_mindRoot);

        var identityLoader = new IdentityLoader();
        var systemMessage = await identityLoader.LoadSystemMessageAsync(_mindRoot);

        var session = await _client.CreateSessionAsync(new SessionConfig
        {
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemMessage
            }
        });

        var response = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = "Say hello in exactly one word." },
            timeout: TimeSpan.FromSeconds(30));

        Assert.NotNull(response);
        Assert.NotNull(response.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Data.Content));
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (Directory.Exists(_mindRoot))
        {
            Directory.Delete(_mindRoot, recursive: true);
        }
    }
}
