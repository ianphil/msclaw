using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsClaw.Models;
using NuGet.Versioning;

namespace MsClaw.Core;

public sealed class ExtensionManager : IExtensionManager
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceProvider _services;
    private readonly IConfigPersistence _configPersistence;
    private readonly MsClawOptions _options;
    private readonly ILogger<ExtensionManager> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly object _stateLock = new();

    private readonly List<LoadedExtension> _loaded = [];
    private readonly List<AIFunction> _tools = [];
    private readonly Dictionary<string, List<HookRegistration>> _hooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommandRegistration> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<IEndpointRouteBuilder>> _httpRoutes = [];
    private bool _initialized;

    public ExtensionManager(
        IServiceProvider services,
        IConfigPersistence configPersistence,
        IOptions<MsClawOptions> options,
        ILogger<ExtensionManager> logger)
    {
        _services = services;
        _configPersistence = configPersistence;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await RegisterCoreExtensionsAsync(cancellationToken);
            await RegisterExternalExtensionsAsync(cancellationToken);

            await FireHookAsync(
                ExtensionEvents.ExtensionLoaded,
                new ExtensionHookContext { EventName = ExtensionEvents.ExtensionLoaded },
                cancellationToken);

            await StartExtensionsAsync(_loaded, cancellationToken);

            await FireHookAsync(
                ExtensionEvents.AgentBootstrap,
                new ExtensionHookContext { EventName = ExtensionEvents.AgentBootstrap },
                cancellationToken);

            _initialized = true;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = GetLoadedSnapshot();
            for (var i = snapshot.Count - 1; i >= 0; i--)
            {
                await StopAndDisposeAsync(snapshot[i], cancellationToken);
            }

            lock (_stateLock)
            {
                _loaded.Clear();
                _tools.Clear();
                _hooks.Clear();
                _commands.Clear();
                _httpRoutes.Clear();
                _initialized = false;
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public async Task ReloadExternalAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            await RemoveExternalExtensionsAsync(cancellationToken);
            await RegisterExternalExtensionsAsync(cancellationToken);

            await FireHookAsync(
                ExtensionEvents.ExtensionLoaded,
                new ExtensionHookContext { EventName = ExtensionEvents.ExtensionLoaded },
                cancellationToken);

            var externalExtensions = GetLoadedSnapshot().Where(l => l.Tier == ExtensionTier.External).ToArray();
            await StartExtensionsAsync(externalExtensions, cancellationToken);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public IReadOnlyList<AIFunction> GetTools()
    {
        lock (_stateLock)
        {
            return _tools.ToArray();
        }
    }

    public IReadOnlyList<LoadedExtensionInfo> GetLoadedExtensions()
    {
        lock (_stateLock)
        {
            return _loaded.Select(l => new LoadedExtensionInfo
            {
                Id = l.Id,
                Name = l.Name,
                Version = l.Version,
                Tier = l.Tier,
                Started = l.Started,
                Failed = l.Failed
            }).ToArray();
        }
    }

    public void MapRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        ArgumentNullException.ThrowIfNull(endpointRouteBuilder);

        Action<IEndpointRouteBuilder>[] routes;
        lock (_stateLock)
        {
            routes = _httpRoutes.ToArray();
        }

        foreach (var route in routes)
        {
            try
            {
                route(endpointRouteBuilder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTTP route mapping failed for an extension route.");
            }
        }
    }

    public async Task FireHookAsync(string eventName, ExtensionHookContext context, CancellationToken cancellationToken = default)
    {
        var normalizedEventName = NormalizeHookEventName(eventName);
        HookRegistration[] handlers;
        lock (_stateLock)
        {
            handlers = _hooks.TryGetValue(normalizedEventName, out var list)
                ? list.ToArray()
                : [];
        }

        foreach (var hook in handlers)
        {
            try
            {
                await hook.Handler(new ExtensionHookContext
                {
                    EventName = normalizedEventName,
                    SessionId = context.SessionId,
                    Message = context.Message,
                    Response = context.Response
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hook '{HookEvent}' failed in extension '{ExtensionId}'.", normalizedEventName, hook.ExtensionId);
            }
        }
    }

    public async Task<string?> TryExecuteCommandAsync(string input, string? sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        ParseCommand(trimmed, out var command, out var arguments);

        CommandRegistration? registration;
        lock (_stateLock)
        {
            _commands.TryGetValue(command, out registration);
        }

        if (registration is null)
        {
            return $"Unknown command: {command}";
        }

        var context = new ExtensionCommandContext
        {
            Command = command,
            Arguments = arguments,
            RawInput = trimmed,
            SessionId = sessionId
        };

        try
        {
            return await registration.Handler(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command '{Command}' failed in extension '{ExtensionId}'.", command, registration.ExtensionId);
            return $"Command failed: {ex.Message}";
        }
    }

    private async Task RegisterCoreExtensionsAsync(CancellationToken cancellationToken)
    {
        var coreDescriptors = new[]
        {
            ExtensionDescriptor.ForCore("msclaw.core.mind-reader", "Mind Reader", GetCurrentVersion(), typeof(MindReaderExtension)),
            ExtensionDescriptor.ForCore("msclaw.core.runtime-control", "Runtime Control", GetCurrentVersion(), typeof(RuntimeControlExtension))
        };

        foreach (var descriptor in coreDescriptors)
        {
            await TryRegisterExtensionAsync(descriptor, cancellationToken);
        }
    }

    private async Task RegisterExternalExtensionsAsync(CancellationToken cancellationToken)
    {
        var disabled = new HashSet<string>(
            _configPersistence.Load()?.DisabledExtensions ?? [],
            StringComparer.OrdinalIgnoreCase);

        var discovered = DiscoverExternalManifests();
        var coreIds = new HashSet<string>(GetLoadedSnapshot().Where(l => l.Tier == ExtensionTier.Core).Select(l => l.Id), StringComparer.OrdinalIgnoreCase);
        var ordered = ResolveExternalLoadOrder(discovered, disabled, coreIds);

        foreach (var descriptor in ordered)
        {
            await TryRegisterExtensionAsync(descriptor, cancellationToken);
        }
    }

    private List<ExtensionDescriptor> DiscoverExternalManifests()
    {
        var appRoot = AppContext.BaseDirectory;
        var mindRoot = Path.GetFullPath(_options.MindRoot);

        var descriptorsById = new Dictionary<string, ExtensionDescriptor>(StringComparer.OrdinalIgnoreCase);
        ScanManifestsInto(descriptorsById, Path.Combine(appRoot, "extensions"), ExtensionTier.External, isMindRoot: false);
        ScanManifestsInto(descriptorsById, Path.Combine(mindRoot, "extensions"), ExtensionTier.External, isMindRoot: true);
        return descriptorsById.Values.ToList();
    }

    private void ScanManifestsInto(
        IDictionary<string, ExtensionDescriptor> descriptorsById,
        string root,
        ExtensionTier tier,
        bool isMindRoot)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories))
        {
            if (!TryReadManifestDescriptor(manifestPath, tier, out var descriptor))
            {
                continue;
            }

            if (descriptorsById.TryGetValue(descriptor.Id, out var existing))
            {
                if (!isMindRoot)
                {
                    _logger.LogWarning("Duplicate extension ID '{ExtensionId}' encountered at '{ManifestPath}'. Keeping first occurrence.", descriptor.Id, manifestPath);
                    continue;
                }

                _logger.LogInformation(
                    "Mind extension '{ExtensionId}' overrides app extension from '{OldPath}' with '{NewPath}'.",
                    descriptor.Id,
                    existing.ManifestPath,
                    descriptor.ManifestPath);
            }

            descriptorsById[descriptor.Id] = descriptor;
        }
    }

    private bool TryReadManifestDescriptor(string manifestPath, ExtensionTier tier, out ExtensionDescriptor descriptor)
    {
        descriptor = default!;

        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), ManifestJsonOptions);
            if (manifest is null)
            {
                _logger.LogWarning("Manifest parse yielded null at '{ManifestPath}'. Skipping extension.", manifestPath);
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.Id)
                || string.IsNullOrWhiteSpace(manifest.Name)
                || string.IsNullOrWhiteSpace(manifest.Version)
                || string.IsNullOrWhiteSpace(manifest.EntryAssembly)
                || string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                _logger.LogWarning("Manifest at '{ManifestPath}' is missing required fields. Skipping extension.", manifestPath);
                return false;
            }

            var extensionRoot = Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidOperationException($"Could not resolve extension root for {manifestPath}");
            var entryAssemblyPath = Path.GetFullPath(Path.Combine(extensionRoot, manifest.EntryAssembly));
            if (!File.Exists(entryAssemblyPath))
            {
                _logger.LogWarning("Entry assembly '{AssemblyPath}' not found for manifest '{ManifestPath}'.", entryAssemblyPath, manifestPath);
                return false;
            }

            descriptor = ExtensionDescriptor.ForExternal(
                id: manifest.Id.Trim(),
                name: manifest.Name.Trim(),
                version: manifest.Version.Trim(),
                entryAssemblyPath: entryAssemblyPath,
                entryType: manifest.EntryType.Trim(),
                dependencies: manifest.Dependencies ?? [],
                config: manifest.Config,
                manifestPath: manifestPath,
                tier: tier);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid manifest JSON at '{ManifestPath}'. Skipping extension.", manifestPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected manifest load failure at '{ManifestPath}'. Skipping extension.", manifestPath);
            return false;
        }
    }

    private IReadOnlyList<ExtensionDescriptor> ResolveExternalLoadOrder(
        IReadOnlyList<ExtensionDescriptor> discovered,
        ISet<string> disabled,
        ISet<string> coreIds)
    {
        var pending = discovered
            .Where(d => !disabled.Contains(d.Id))
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var disabledId in disabled)
        {
            _logger.LogInformation("Extension '{ExtensionId}' is disabled via config.", disabledId);
        }

        var result = new List<ExtensionDescriptor>();
        var availableVersions = BuildAvailableVersionMap(discovered, coreIds);

        while (pending.Count > 0)
        {
            var skippedInPass = new List<string>();

            foreach (var candidate in pending.Values.ToArray())
            {
                if (HasPermanentDependencyFailure(candidate, pending, availableVersions, coreIds))
                {
                    skippedInPass.Add(candidate.Id);
                }
            }

            foreach (var skipped in skippedInPass)
            {
                pending.Remove(skipped);
            }

            if (pending.Count == 0)
            {
                break;
            }

            var loadedOrCore = new HashSet<string>(
                result.Select(r => r.Id).Concat(coreIds),
                StringComparer.OrdinalIgnoreCase);

            var ready = pending.Values
                .Where(candidate => candidate.Dependencies.All(d => loadedOrCore.Contains(d.Id)))
                .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ready.Length == 0)
            {
                _logger.LogError(
                    "Circular or unresolved external extension dependencies detected for: {ExtensionIds}",
                    string.Join(", ", pending.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
                break;
            }

            foreach (var descriptor in ready)
            {
                result.Add(descriptor);
                pending.Remove(descriptor.Id);
            }
        }

        return result;
    }

    private Dictionary<string, NuGetVersion> BuildAvailableVersionMap(IReadOnlyList<ExtensionDescriptor> discovered, ISet<string> coreIds)
    {
        var versions = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);

        foreach (var coreId in coreIds)
        {
            if (NuGetVersion.TryParse(GetCurrentVersion(), out var coreVersion))
            {
                versions[coreId] = coreVersion;
            }
        }

        foreach (var descriptor in discovered)
        {
            if (!NuGetVersion.TryParse(descriptor.Version, out var version))
            {
                continue;
            }

            versions[descriptor.Id] = version;
        }

        return versions;
    }

    private bool HasPermanentDependencyFailure(
        ExtensionDescriptor candidate,
        IReadOnlyDictionary<string, ExtensionDescriptor> pending,
        IReadOnlyDictionary<string, NuGetVersion> versions,
        ISet<string> coreIds)
    {
        foreach (var dependency in candidate.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.Id))
            {
                _logger.LogError("Extension '{ExtensionId}' has dependency with missing ID. Skipping extension.", candidate.Id);
                return true;
            }

            var depId = dependency.Id.Trim();
            var knownDependency = pending.ContainsKey(depId) || coreIds.Contains(depId);
            if (!knownDependency)
            {
                _logger.LogError("Extension '{ExtensionId}' has missing dependency '{DependencyId}'. Skipping extension.", candidate.Id, depId);
                return true;
            }

            if (!versions.TryGetValue(depId, out var depVersion))
            {
                _logger.LogError("Extension '{ExtensionId}' has dependency '{DependencyId}' with invalid version. Skipping extension.", candidate.Id, depId);
                return true;
            }

            if (!VersionRange.TryParse(dependency.Range, out var range))
            {
                _logger.LogError(
                    "Extension '{ExtensionId}' dependency '{DependencyId}' has invalid range '{Range}'. Skipping extension.",
                    candidate.Id,
                    depId,
                    dependency.Range);
                return true;
            }

            if (!range.Satisfies(depVersion))
            {
                _logger.LogError(
                    "Extension '{ExtensionId}' dependency '{DependencyId}' requires '{Range}' but found '{Version}'. Skipping extension.",
                    candidate.Id,
                    depId,
                    dependency.Range,
                    depVersion.ToNormalizedString());
                return true;
            }
        }

        return false;
    }

    private async Task TryRegisterExtensionAsync(ExtensionDescriptor descriptor, CancellationToken cancellationToken)
    {
        if (GetLoadedSnapshot().Any(loaded => loaded.Id.Equals(descriptor.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Extension '{ExtensionId}' already loaded. Skipping duplicate registration.", descriptor.Id);
            return;
        }

        AssemblyLoadContext? loadContext = null;
        IExtension extensionInstance;
        try
        {
            if (descriptor.CoreType is not null)
            {
                extensionInstance = (IExtension)ActivatorUtilities.CreateInstance(_services, descriptor.CoreType);
            }
            else
            {
                loadContext = new ExtensionLoadContext(descriptor.EntryAssemblyPath);
                var assembly = loadContext.LoadFromAssemblyPath(descriptor.EntryAssemblyPath);
                var extensionType = assembly.GetType(descriptor.EntryType, throwOnError: false, ignoreCase: false);
                if (extensionType is null || !typeof(IExtension).IsAssignableFrom(extensionType))
                {
                    _logger.LogError(
                        "Entry type '{EntryType}' in extension '{ExtensionId}' does not implement IExtension. Skipping extension.",
                        descriptor.EntryType,
                        descriptor.Id);
                    loadContext.Unload();
                    return;
                }

                extensionInstance = (IExtension)ActivatorUtilities.CreateInstance(_services, extensionType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Constructor failure while loading extension '{ExtensionId}'. Skipping extension.", descriptor.Id);
            loadContext?.Unload();
            return;
        }

        var registration = new ExtensionRegistration();
        var api = new ExtensionRegistrationApi(descriptor.Id, descriptor.Config, registration);

        try
        {
            extensionInstance.Register(api);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Register() failed for extension '{ExtensionId}'. Skipping extension.", descriptor.Id);
            await extensionInstance.DisposeAsync();
            loadContext?.Unload();
            return;
        }

        var loaded = new LoadedExtension
        {
            Id = descriptor.Id,
            Name = descriptor.Name,
            Version = descriptor.Version,
            Tier = descriptor.Tier,
            Instance = extensionInstance,
            Registration = registration,
            LoadContext = loadContext
        };

        lock (_stateLock)
        {
            _loaded.Add(loaded);
            _tools.AddRange(registration.Tools);
            _httpRoutes.AddRange(registration.HttpRoutes);

            foreach (var hook in registration.Hooks)
            {
                var key = NormalizeHookEventName(hook.EventName);
                if (!_hooks.TryGetValue(key, out var handlers))
                {
                    handlers = [];
                    _hooks[key] = handlers;
                }

                handlers.Add(new HookRegistration(descriptor.Id, key, hook.Handler));
            }

            foreach (var command in registration.Commands)
            {
                _commands[NormalizeCommand(command.Command)] = new CommandRegistration(descriptor.Id, command.Handler);
            }
        }

        _logger.LogInformation("Loaded extension '{ExtensionId}' ({Tier}).", descriptor.Id, descriptor.Tier);
    }

    private async Task StartExtensionsAsync(IEnumerable<LoadedExtension> extensions, CancellationToken cancellationToken)
    {
        foreach (var loaded in extensions)
        {
            if (loaded.Started || loaded.Failed)
            {
                continue;
            }

            try
            {
                await loaded.Instance.StartAsync(cancellationToken);
                loaded.Started = true;
            }
            catch (Exception ex)
            {
                loaded.Failed = true;
                _logger.LogError(ex, "StartAsync() failed for extension '{ExtensionId}'. Continuing startup.", loaded.Id);
                continue;
            }

            foreach (var service in loaded.Registration.Services)
            {
                try
                {
                    await service.StartAsync(cancellationToken);
                    loaded.StartedServices.Add(service);
                }
                catch (Exception ex)
                {
                    loaded.Failed = true;
                    _logger.LogError(ex, "Service start failed in extension '{ExtensionId}'. Continuing startup.", loaded.Id);
                }
            }
        }
    }

    private async Task RemoveExternalExtensionsAsync(CancellationToken cancellationToken)
    {
        var external = GetLoadedSnapshot().Where(l => l.Tier == ExtensionTier.External).ToList();
        for (var i = external.Count - 1; i >= 0; i--)
        {
            var extension = external[i];
            await StopAndDisposeAsync(extension, cancellationToken);
            RemoveRegistration(extension);
        }
    }

    private void RemoveRegistration(LoadedExtension extension)
    {
        lock (_stateLock)
        {
            _loaded.Remove(extension);

            foreach (var tool in extension.Registration.Tools)
            {
                _tools.Remove(tool);
            }

            foreach (var route in extension.Registration.HttpRoutes)
            {
                _httpRoutes.Remove(route);
            }

            foreach (var hook in extension.Registration.Hooks)
            {
                var key = NormalizeHookEventName(hook.EventName);
                if (_hooks.TryGetValue(key, out var handlers))
                {
                    handlers.RemoveAll(h => h.ExtensionId.Equals(extension.Id, StringComparison.OrdinalIgnoreCase) && h.Handler == hook.Handler);
                    if (handlers.Count == 0)
                    {
                        _hooks.Remove(key);
                    }
                }
            }

            foreach (var command in extension.Registration.Commands)
            {
                var normalized = NormalizeCommand(command.Command);
                if (_commands.TryGetValue(normalized, out var registered)
                    && registered.ExtensionId.Equals(extension.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _commands.Remove(normalized);
                }
            }
        }
    }

    private async Task StopAndDisposeAsync(LoadedExtension extension, CancellationToken cancellationToken)
    {
        for (var i = extension.StartedServices.Count - 1; i >= 0; i--)
        {
            try
            {
                await extension.StartedServices[i].StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service stop failed in extension '{ExtensionId}'.", extension.Id);
            }
        }

        if (extension.Started)
        {
            try
            {
                await extension.Instance.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StopAsync() failed for extension '{ExtensionId}'.", extension.Id);
            }
        }

        try
        {
            await extension.Instance.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DisposeAsync() failed for extension '{ExtensionId}'.", extension.Id);
        }

        extension.LoadContext?.Unload();
    }

    private List<LoadedExtension> GetLoadedSnapshot()
    {
        lock (_stateLock)
        {
            return _loaded.ToList();
        }
    }

    private static string NormalizeHookEventName(string eventName) => eventName.Trim().ToLowerInvariant();

    private static string NormalizeCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return "/";
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }

    private static void ParseCommand(string input, out string command, out string arguments)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        command = NormalizeCommand(parts[0]);
        arguments = parts.Length > 1 ? parts[1] : "";
    }

    private static string GetCurrentVersion()
    {
        return typeof(ExtensionManager).Assembly
                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? typeof(ExtensionManager).Assembly.GetName().Version?.ToString()
               ?? "0.0.0";
    }

    private sealed class ExtensionRegistrationApi : IMsClawPluginApi
    {
        private readonly ExtensionRegistration _registration;

        public ExtensionRegistrationApi(string extensionId, JsonElement? config, ExtensionRegistration registration)
        {
            ExtensionId = extensionId;
            Config = config;
            _registration = registration;
        }

        public string ExtensionId { get; }
        public JsonElement? Config { get; }

        public void RegisterTool(AIFunction tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            _registration.Tools.Add(tool);
        }

        public void RegisterHook(string eventName, ExtensionHookHandler handler)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                throw new ArgumentException("Hook event name is required.", nameof(eventName));
            }

            ArgumentNullException.ThrowIfNull(handler);
            _registration.Hooks.Add(new HookRegistrationDescriptor(eventName, handler));
        }

        public void RegisterService(IHostedService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            _registration.Services.Add(service);
        }

        public void RegisterCommand(string command, ExtensionCommandHandler handler)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command name is required.", nameof(command));
            }

            ArgumentNullException.ThrowIfNull(handler);
            _registration.Commands.Add(new CommandRegistrationDescriptor(command, handler));
        }

        public void RegisterHttpRoute(Action<IEndpointRouteBuilder> mapRoute)
        {
            ArgumentNullException.ThrowIfNull(mapRoute);
            _registration.HttpRoutes.Add(mapRoute);
        }
    }

    private sealed class ExtensionDescriptor
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required ExtensionTier Tier { get; init; }
        public string? ManifestPath { get; init; }
        public Type? CoreType { get; init; }
        public string EntryAssemblyPath { get; init; } = "";
        public string EntryType { get; init; } = "";
        public JsonElement? Config { get; init; }
        public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];

        public static ExtensionDescriptor ForCore(string id, string name, string version, Type type)
            => new()
            {
                Id = id,
                Name = name,
                Version = version,
                Tier = ExtensionTier.Core,
                CoreType = type
            };

        public static ExtensionDescriptor ForExternal(
            string id,
            string name,
            string version,
            string entryAssemblyPath,
            string entryType,
            IReadOnlyList<PluginDependency> dependencies,
            JsonElement? config,
            string manifestPath,
            ExtensionTier tier)
            => new()
            {
                Id = id,
                Name = name,
                Version = version,
                Tier = tier,
                EntryAssemblyPath = entryAssemblyPath,
                EntryType = entryType,
                Dependencies = dependencies,
                Config = config,
                ManifestPath = manifestPath
            };
    }

    private sealed class ExtensionRegistration
    {
        public List<AIFunction> Tools { get; } = [];
        public List<HookRegistrationDescriptor> Hooks { get; } = [];
        public List<IHostedService> Services { get; } = [];
        public List<CommandRegistrationDescriptor> Commands { get; } = [];
        public List<Action<IEndpointRouteBuilder>> HttpRoutes { get; } = [];
    }

    private sealed class LoadedExtension
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required ExtensionTier Tier { get; init; }
        public required IExtension Instance { get; init; }
        public required ExtensionRegistration Registration { get; init; }
        public AssemblyLoadContext? LoadContext { get; init; }
        public bool Started { get; set; }
        public bool Failed { get; set; }
        public List<IHostedService> StartedServices { get; } = [];
    }

    private sealed record HookRegistrationDescriptor(string EventName, ExtensionHookHandler Handler);

    private sealed record CommandRegistrationDescriptor(string Command, ExtensionCommandHandler Handler);

    private sealed record HookRegistration(string ExtensionId, string EventName, ExtensionHookHandler Handler);

    private sealed record CommandRegistration(string ExtensionId, ExtensionCommandHandler Handler);
}

internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public ExtensionLoadContext(string mainAssemblyPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}

internal sealed class MindReaderExtension : ExtensionBase
{
    private readonly IMindReader _mindReader;

    public MindReaderExtension(IMindReader mindReader)
    {
        _mindReader = mindReader;
    }

    public override void Register(IMsClawPluginApi api)
    {
        api.RegisterTool(AIFunctionFactory.Create(ReadMindFileAsync));
        api.RegisterTool(AIFunctionFactory.Create(ListMindDirectoryAsync));
    }

    [Description("Read a file from the active mind root.")]
    public Task<string> ReadMindFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return _mindReader.ReadFileAsync(path, cancellationToken);
    }

    [Description("List entries in a directory from the active mind root.")]
    public async Task<string[]> ListMindDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        var entries = await _mindReader.ListDirectoryAsync(path, cancellationToken);
        return entries.ToArray();
    }
}

internal sealed class RuntimeControlExtension : ExtensionBase
{
    private readonly IExtensionManager _extensionManager;
    private readonly IServiceProvider _services;

    public RuntimeControlExtension(IExtensionManager extensionManager, IServiceProvider services)
    {
        _extensionManager = extensionManager;
        _services = services;
    }

    public override void Register(IMsClawPluginApi api)
    {
        api.RegisterCommand("/reload", ReloadAsync);
        api.RegisterCommand("/extensions", ListExtensionsAsync);
        api.RegisterHttpRoute(MapRoutes);
    }

    private void MapRoutes(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/extensions", () => Results.Ok(_extensionManager.GetLoadedExtensions()));
    }

    private async Task<string> ReloadAsync(ExtensionCommandContext context, CancellationToken cancellationToken)
    {
        await _extensionManager.ReloadExternalAsync(cancellationToken);
        var sessionControl = _services.GetService<ISessionControl>();
        if (sessionControl is not null)
        {
            await sessionControl.CycleSessionsAsync(cancellationToken);
        }

        return "External extensions reloaded.";
    }

    private Task<string> ListExtensionsAsync(ExtensionCommandContext context, CancellationToken cancellationToken)
    {
        var rows = _extensionManager.GetLoadedExtensions()
            .OrderBy(x => x.Tier)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Tier.ToString().ToLowerInvariant()}: {x.Id} {x.Version} started={x.Started} failed={x.Failed}");

        return Task.FromResult(string.Join(Environment.NewLine, rows));
    }
}
