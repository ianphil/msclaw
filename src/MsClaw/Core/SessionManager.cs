using System.Text.Json;
using MsClaw.Models;
using Microsoft.Extensions.Options;

namespace MsClaw.Core;

public sealed class SessionManager : ISessionManager
{
    private const string ActiveSessionFile = "active-session-id.txt";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _sessionStore;

    public SessionManager(IOptions<MsClawOptions> options)
    {
        _sessionStore = Path.GetFullPath(options.Value.SessionStore);
    }

    public async Task<SessionState> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_sessionStore);
        var activeSessionPath = Path.Combine(_sessionStore, ActiveSessionFile);

        if (File.Exists(activeSessionPath))
        {
            var sessionId = (await File.ReadAllTextAsync(activeSessionPath, cancellationToken)).Trim();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var filePath = GetSessionPath(sessionId);
                if (File.Exists(filePath))
                {
                    await using var stream = File.OpenRead(filePath);
                    var session = await JsonSerializer.DeserializeAsync<SessionState>(stream, cancellationToken: cancellationToken);
                    if (session is not null)
                    {
                        return session;
                    }
                }
            }
        }

        return await CreateNewAsync(cancellationToken);
    }

    public async Task<SessionState> CreateNewAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_sessionStore);

        var session = new SessionState
        {
            SessionId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await SaveAsync(session, cancellationToken);
        return session;
    }

    public async Task SaveAsync(SessionState session, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_sessionStore);

        var activeSessionPath = Path.Combine(_sessionStore, ActiveSessionFile);
        await File.WriteAllTextAsync(activeSessionPath, session.SessionId, cancellationToken);

        var filePath = GetSessionPath(session.SessionId);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
    }

    private string GetSessionPath(string sessionId) => Path.Combine(_sessionStore, $"{sessionId}.json");
}
