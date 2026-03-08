using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using MsClaw.Core;

namespace MsClaw.Gateway.Extensions;

/// <summary>
/// Intercepts requests for <c>/</c> and <c>/index.html</c>, reads the static file from disk,
/// and injects a <c>&lt;script&gt;</c> block containing the auth context from
/// <see cref="IUserConfigLoader"/> so the browser UI never needs to call a token endpoint.
/// </summary>
public sealed class AuthContextMiddleware(RequestDelegate next, IUserConfigLoader userConfigLoader, IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Processes the HTTP request, injecting auth context into <c>index.html</c> when applicable.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (context.Request.Method is not "GET"
            || (path is not "/" and not "/index.html"))
        {
            await next(context);
            return;
        }

        var fileInfo = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!fileInfo.Exists)
        {
            await next(context);
            return;
        }

        var html = await ReadFileAsync(fileInfo);
        var scriptBlock = BuildInjectedScript();
        html = html.Replace("</head>", $"{scriptBlock}\n</head>", StringComparison.Ordinal);

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html);
    }

    private string BuildInjectedScript()
    {
        var config = userConfigLoader.Load();
        if (GatewayEndpointExtensions.TryGetValidAuth(config, DateTimeOffset.UtcNow, out var authConfig))
        {
            var payload = new
            {
                authenticated = true,
                username = authConfig!.Username,
                accessToken = authConfig.AccessToken,
                expiresAtUtc = authConfig.ExpiresAtUtc
            };
            var json = JsonSerializer.Serialize(payload, s_jsonOptions);

            return $"    <script>window.__AUTH_CONTEXT = {json};</script>";
        }

        return "    <script>window.__AUTH_CONTEXT = null;</script>";
    }

    private static async Task<string> ReadFileAsync(IFileInfo fileInfo)
    {
        await using var stream = fileInfo.CreateReadStream();
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }
}
