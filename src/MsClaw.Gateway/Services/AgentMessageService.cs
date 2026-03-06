using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Coordinates message-related gateway dependencies for future hub and HTTP orchestration.
/// </summary>
public sealed class AgentMessageService
{
    /// <summary>
    /// Initializes the service with the shared coordination and hosting dependencies.
    /// </summary>
    public AgentMessageService(
        IConcurrencyGate concurrencyGate,
        ISessionMap sessionMap,
        IGatewayHostedService hostedService)
    {
        ConcurrencyGate = concurrencyGate;
        SessionMap = sessionMap;
        HostedService = hostedService;
    }

    /// <summary>
    /// Gets the shared concurrency gate used for caller coordination.
    /// </summary>
    public IConcurrencyGate ConcurrencyGate { get; }

    /// <summary>
    /// Gets the shared session map used for caller-to-session tracking.
    /// </summary>
    public ISessionMap SessionMap { get; }

    /// <summary>
    /// Gets the hosted gateway service that owns runtime startup state.
    /// </summary>
    public IGatewayHostedService HostedService { get; }
}
