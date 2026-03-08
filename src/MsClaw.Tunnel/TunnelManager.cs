using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsClaw.Core;

namespace MsClaw.Tunnel;

/// <summary>
/// Manages persistent dev tunnel creation and host process lifecycle.
/// </summary>
public sealed class TunnelManager : ITunnelManager
{
    private static readonly Regex UrlRegex = new(@"https://[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TunnelIdRegex = new(@"(?:tunnel[\s_-]*id\s*[:=]\s*|id\s*[:=]\s*)(?<id>[a-zA-Z0-9-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IDevTunnelLocator locator;
    private readonly IDevTunnelExecutor executor;
    private readonly IUserConfigLoader configLoader;
    private readonly TunnelManagerOptions options;
    private readonly ILogger<TunnelManager> logger;
    private readonly object gate = new();

    private IDevTunnelHostHandle? hostHandle;
    private string? tunnelId;
    private string? publicUrl;
    private string? error;

    /// <summary>
    /// Creates a new tunnel manager.
    /// </summary>
    public TunnelManager(
        IDevTunnelLocator locator,
        IUserConfigLoader configLoader,
        TunnelManagerOptions options,
        ILogger<TunnelManager>? logger = null)
        : this(locator, configLoader, options, new ProcessDevTunnelExecutor(), logger)
    {
    }

    internal TunnelManager(
        IDevTunnelLocator locator,
        IUserConfigLoader configLoader,
        TunnelManagerOptions options,
        IDevTunnelExecutor executor,
        ILogger<TunnelManager>? logger = null)
    {
        this.locator = locator;
        this.configLoader = configLoader;
        this.options = options;
        this.executor = executor;
        this.logger = logger ?? NullLogger<TunnelManager>.Instance;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (options.Enabled is false)
        {
            return;
        }

        lock (gate)
        {
            if (hostHandle is not null && hostHandle.HasExited is false)
            {
                return;
            }
        }

        var cliPath = locator.ResolveDevTunnelCliPath();
        await EnsureAuthenticatedAsync(cliPath, cancellationToken);

        var resolvedTunnelId = await ResolveTunnelIdAsync(cliPath, cancellationToken);
        await EnsureTunnelReadyAsync(cliPath, resolvedTunnelId, cancellationToken);
        var (handle, resolvedPublicUrl) = await StartHostAndWaitForUrlAsync(cliPath, resolvedTunnelId, cancellationToken);

        lock (gate)
        {
            hostHandle = handle;
            tunnelId = resolvedTunnelId;
            publicUrl = resolvedPublicUrl;
            error = null;
        }

        PersistTunnelId(resolvedTunnelId);
        logger.LogInformation("Dev tunnel started at {PublicUrl}", resolvedPublicUrl);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        IDevTunnelHostHandle? handle;
        lock (gate)
        {
            handle = hostHandle;
            hostHandle = null;
        }

        if (handle is null)
        {
            return;
        }

        await handle.StopAsync(cancellationToken);
        await handle.DisposeAsync();

        lock (gate)
        {
            publicUrl = null;
        }
    }

    /// <inheritdoc />
    public TunnelStatus GetStatus()
    {
        lock (gate)
        {
            return new TunnelStatus
            {
                Enabled = options.Enabled,
                IsRunning = hostHandle is not null && hostHandle.HasExited is false,
                TunnelId = tunnelId,
                PublicUrl = publicUrl,
                Error = error
            };
        }
    }

    /// <summary>
    /// Resolves the tunnel ID from options/config, or creates a new tunnel if none is configured.
    /// </summary>
    private async Task<string> ResolveTunnelIdAsync(string cliPath, CancellationToken cancellationToken)
    {
        var configuredTunnelId = ResolveConfiguredTunnelId();

        return string.IsNullOrWhiteSpace(configuredTunnelId)
            ? await CreateTunnelAsync(cliPath, cancellationToken)
            : configuredTunnelId;
    }

    /// <summary>
    /// Ensures the tunnel has tenant-scoped access and the configured port is registered.
    /// </summary>
    private async Task EnsureTunnelReadyAsync(string cliPath, string resolvedTunnelId, CancellationToken cancellationToken)
    {
        await EnsureTunnelAccessAsync(cliPath, resolvedTunnelId, cancellationToken);
        await EnsureTunnelPortAsync(cliPath, resolvedTunnelId, cancellationToken);
    }

    /// <summary>
    /// Starts the devtunnel host process and waits for the public URL to appear in stdout.
    /// </summary>
    private async Task<(IDevTunnelHostHandle Handle, string PublicUrl)> StartHostAndWaitForUrlAsync(
        string cliPath,
        string resolvedTunnelId,
        CancellationToken cancellationToken)
    {
        var urlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrLines = new List<string>();
        var handle = executor.CreateHost(cliPath, $"host {resolvedTunnelId}");
        handle.OutputLine += line =>
        {
            logger.LogInformation("devtunnel: {Line}", line);
            var parsed = TryParsePublicUrl(line);
            if (parsed is not null)
            {
                _ = urlTcs.TrySetResult(parsed);
            }
        };
        handle.ErrorLine += line =>
        {
            logger.LogWarning("devtunnel stderr: {Line}", line);
            lock (stderrLines)
            {
                stderrLines.Add(line);
            }
        };

        handle.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

        string resolvedPublicUrl;
        try
        {
            resolvedPublicUrl = await urlTcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            var stderr = string.Join(Environment.NewLine, stderrLines);
            await handle.DisposeAsync();
            throw new InvalidOperationException(
                $"devtunnel host did not emit a public URL within timeout for tunnel '{resolvedTunnelId}'. {stderr}".Trim());
        }

        return (handle, resolvedPublicUrl);
    }

    private async Task EnsureAuthenticatedAsync(string cliPath, CancellationToken cancellationToken)
    {
        var result = await executor.RunAsync(cliPath, "user show", cancellationToken);
        EnsureSuccess(result, "devtunnel user show");
    }

    private string ResolveConfiguredTunnelId()
    {
        if (string.IsNullOrWhiteSpace(options.TunnelId) is false)
        {
            return options.TunnelId;
        }

        var config = configLoader.Load();

        return config.TunnelId ?? string.Empty;
    }

    private async Task<string> CreateTunnelAsync(string cliPath, CancellationToken cancellationToken)
    {
        var createResult = await executor.RunAsync(cliPath, "create", cancellationToken);
        EnsureSuccess(createResult, "devtunnel create");
        var createdTunnelId = TryParseTunnelId(createResult.StandardOutput);
        if (string.IsNullOrWhiteSpace(createdTunnelId))
        {
            throw new InvalidOperationException(
                $"Unable to parse tunnel ID from 'devtunnel create' output: {createResult.StandardOutput}");
        }

        return createdTunnelId;
    }

    private async Task EnsureTunnelAccessAsync(string cliPath, string resolvedTunnelId, CancellationToken cancellationToken)
    {
        var accessResult = await executor.RunAsync(cliPath, $"access create {resolvedTunnelId} --tenant", cancellationToken);
        EnsureSuccess(accessResult, $"devtunnel access create {resolvedTunnelId} --tenant");
    }

    private async Task EnsureTunnelPortAsync(string cliPath, string resolvedTunnelId, CancellationToken cancellationToken)
    {
        var portResult = await executor.RunAsync(cliPath, $"port create {resolvedTunnelId} -p {options.LocalPort}", cancellationToken);
        if (IsPortAlreadyPresentConflict(portResult))
        {
            logger.LogInformation("Dev tunnel port {Port} already exists on tunnel {TunnelId}; continuing.", options.LocalPort, resolvedTunnelId);
            return;
        }

        EnsureSuccess(portResult, $"devtunnel port create {resolvedTunnelId} -p {options.LocalPort}");
    }

    private void PersistTunnelId(string resolvedTunnelId)
    {
        var currentConfig = configLoader.Load();
        if (string.Equals(currentConfig.TunnelId, resolvedTunnelId, StringComparison.Ordinal))
        {
            return;
        }

        currentConfig.TunnelId = resolvedTunnelId;
        configLoader.Save(currentConfig);
    }

    private static string? TryParsePublicUrl(string line)
    {
        var match = UrlRegex.Match(line);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Extracts a tunnel ID from command output, trying JSON, regex, and URL-based strategies in order.
    /// </summary>
    private static string? TryParseTunnelId(string output)
    {
        return TryParseTunnelIdFromJson(output)
            ?? TryParseTunnelIdFromRegex(output)
            ?? TryParseTunnelIdFromUrl(output);
    }

    /// <summary>
    /// Attempts to parse a tunnel ID from JSON output containing a "tunnelId" or "id" property.
    /// </summary>
    private static string? TryParseTunnelIdFromJson(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.TryGetProperty("tunnelId", out var tunnelIdProperty))
            {
                return tunnelIdProperty.GetString();
            }

            if (root.TryGetProperty("id", out var idProperty))
            {
                return idProperty.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to other strategies.
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a tunnel ID using a regex pattern like "tunnel id: abc-123".
    /// </summary>
    private static string? TryParseTunnelIdFromRegex(string output)
    {
        var match = TunnelIdRegex.Match(output);

        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// Attempts to extract a tunnel ID from the first segment of a URL hostname.
    /// </summary>
    private static string? TryParseTunnelIdFromUrl(string output)
    {
        var match = UrlRegex.Match(output);
        if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var parsedUri))
        {
            return parsedUri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        return null;
    }

    private static bool IsPortAlreadyPresentConflict(DevTunnelCommandResult result)
    {
        if (result.ExitCode == 0)
        {
            return false;
        }

        return result.StandardError.Contains("Conflict with existing entity", StringComparison.OrdinalIgnoreCase)
            && result.StandardError.Contains("port", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureSuccess(DevTunnelCommandResult result, string commandName)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var message = $"{commandName} failed with exit code {result.ExitCode}: {result.StandardError}".Trim();
        lock (gate)
        {
            error = message;
        }

        throw new InvalidOperationException(message);
    }
}
