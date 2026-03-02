using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MsClaw.Core;

internal sealed class RuntimeControlExtension : ExtensionBase
{
    private readonly IExtensionManager _extensionManager;

    public RuntimeControlExtension(IExtensionManager extensionManager)
    {
        _extensionManager = extensionManager;
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
