using Microsoft.Extensions.AI;
using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Defines the session-aware factory that creates the per-session expand_tools function.
/// </summary>
public interface IToolExpander
{
    /// <summary>
    /// Creates an expand_tools function bound to the supplied session holder and mutable tool list.
    /// </summary>
    AIFunction CreateExpandToolsFunction(SessionHolder sessionHolder, IList<AIFunction> currentSessionTools);
}

/// <summary>
/// Provides deferred session binding so expand_tools can await a session created later in the pipeline.
/// </summary>
public sealed class SessionHolder
{
    private readonly TaskCompletionSource<IGatewaySession> sessionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Binds the gateway session and releases any callers waiting for it.
    /// </summary>
    public void Bind(IGatewaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        sessionSource.SetResult(session);
    }

    /// <summary>
    /// Returns the bound gateway session, awaiting binding when necessary.
    /// </summary>
    public Task<IGatewaySession> GetSessionAsync()
    {
        return sessionSource.Task;
    }
}
