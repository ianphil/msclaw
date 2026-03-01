using MsClaw.Core;
using MsClaw.Models;

var builder = WebApplication.CreateBuilder(args);

string resolvedMindRoot;
try
{
    // Bootstrap: resolve mind root before starting the server
    var validator = new MindValidator();
    var configPersistence = new ConfigPersistence();
    var discovery = new MindDiscovery(configPersistence, validator);
    var scaffold = new MindScaffold();
    var orchestrator = new BootstrapOrchestrator(validator, discovery, scaffold, configPersistence);

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

builder.Services.AddSingleton<IMindValidator, MindValidator>();
builder.Services.AddSingleton<IConfigPersistence, ConfigPersistence>();
builder.Services.AddSingleton<IMindDiscovery, MindDiscovery>();
builder.Services.AddSingleton<IMindScaffold, MindScaffold>();
builder.Services.AddSingleton<IIdentityLoader, IdentityLoader>();

builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddSingleton<IMindReader, MindReader>();
builder.Services.AddSingleton<ICopilotRuntimeClient, CopilotRuntimeClient>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/session/new", async (ISessionManager sessionManager, CancellationToken cancellationToken) =>
{
    var session = await sessionManager.CreateNewAsync(cancellationToken);
    return Results.Ok(new { sessionId = session.SessionId });
});

app.MapPost("/chat", async (
    ChatRequest request,
    ISessionManager sessionManager,
    ICopilotRuntimeClient copilotClient,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var session = await sessionManager.GetOrCreateAsync(cancellationToken);

    session.Messages.Add(new SessionMessage
    {
        Role = "user",
        Content = request.Message,
        Timestamp = DateTime.UtcNow
    });

    var assistantResponse = await copilotClient.GetAssistantResponseAsync(session.Messages, cancellationToken);

    session.Messages.Add(new SessionMessage
    {
        Role = "assistant",
        Content = assistantResponse,
        Timestamp = DateTime.UtcNow
    });

    session.UpdatedAt = DateTime.UtcNow;
    await sessionManager.SaveAsync(session, cancellationToken);

    return Results.Ok(new ChatResponse
    {
        Response = assistantResponse,
        SessionId = session.SessionId
    });
});

app.Run();
