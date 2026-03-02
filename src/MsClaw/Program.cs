using MsClaw.Core;
using MsClaw.Models;
using GitHub.Copilot.SDK;

var builder = WebApplication.CreateBuilder(args);

// Bootstrap: resolve mind root before starting the server
var validator = new MindValidator();
var configPersistence = new ConfigPersistence();
var discovery = new MindDiscovery(configPersistence, validator);
var scaffold = new MindScaffold();
var orchestrator = new BootstrapOrchestrator(validator, discovery, scaffold, configPersistence);

string resolvedMindRoot;
try
{
    var bootstrapResult = orchestrator.Run(args);
    if (bootstrapResult is null)
    {
        // --reset-config was used, exit cleanly
        return;
    }

    resolvedMindRoot = bootstrapResult.MindRoot;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return;
}

builder.Services.Configure<MsClawOptions>(builder.Configuration);
builder.Services.Configure<MsClawOptions>(opts =>
{
    opts.MindRoot = resolvedMindRoot;
});

// Register the same instances used during bootstrap — avoids duplicate instantiation
builder.Services.AddSingleton<IMindValidator>(validator);
builder.Services.AddSingleton<IConfigPersistence>(configPersistence);
builder.Services.AddSingleton<IMindDiscovery>(discovery);
builder.Services.AddSingleton<IMindScaffold>(scaffold);
builder.Services.AddSingleton<IIdentityLoader, IdentityLoader>();
builder.Services.AddSingleton<IMindReader, MindReader>();
builder.Services.AddSingleton<ExtensionManager>();
builder.Services.AddSingleton<IExtensionManager>(sp => sp.GetRequiredService<ExtensionManager>());

// Register CopilotClient as singleton
builder.Services.AddSingleton<CopilotClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MsClawOptions>>().Value;
    return new CopilotClient(new CopilotClientOptions
    {
        Cwd = Path.GetFullPath(options.MindRoot),
        AutoStart = true,
        UseStdio = true
    });
});

builder.Services.AddSingleton<CopilotRuntimeClient>();
builder.Services.AddSingleton<ICopilotRuntimeClient>(sp => sp.GetRequiredService<CopilotRuntimeClient>());
builder.Services.AddSingleton<ISessionControl>(sp => sp.GetRequiredService<CopilotRuntimeClient>());

var app = builder.Build();
var extensionManager = app.Services.GetRequiredService<IExtensionManager>();
await extensionManager.InitializeAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/session/new", async (ICopilotRuntimeClient copilotClient, CancellationToken cancellationToken) =>
{
    var sessionId = await copilotClient.CreateSessionAsync(cancellationToken);
    return Results.Ok(new { sessionId });
});

app.MapPost("/command", async (
    ChatRequest request,
    IExtensionManager extensionManager,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var result = await extensionManager.TryExecuteCommandAsync(request.Message, request.SessionId, cancellationToken);
    if (result is null)
    {
        return Results.BadRequest(new { error = "input is not a command" });
    }

    return Results.Ok(new { response = result });
});

app.MapPost("/chat", async (
    ChatRequest request,
    ICopilotRuntimeClient copilotClient,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var sessionId = request.SessionId;

    if (string.IsNullOrWhiteSpace(sessionId))
    {
        sessionId = await copilotClient.CreateSessionAsync(cancellationToken);
    }

    var response = await copilotClient.SendMessageAsync(sessionId, request.Message, cancellationToken);

    return Results.Ok(new ChatResponse
    {
        Response = response,
        SessionId = sessionId
    });
});

extensionManager.MapRoutes(app);
try
{
    await app.RunAsync();
}
finally
{
    await extensionManager.ShutdownAsync();
}
