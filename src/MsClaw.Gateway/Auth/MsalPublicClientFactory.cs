using Microsoft.Identity.Client;

namespace MsClaw.Gateway.Auth;

/// <summary>
/// Creates MSAL public client applications configured with persistent token cache storage.
/// </summary>
public interface IMsalPublicClientFactory
{
    /// <summary>
    /// Builds a configured MSAL public client application for the provided tenant and client IDs.
    /// </summary>
    Task<IPublicClientApplication> CreateAsync(string tenantId, string clientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides the canonical scope list used by interactive login and silent refresh.
/// </summary>
public static class AuthScopes
{
    private static readonly string[] BaseScopes = ["openid", "profile", "offline_access"];

    /// <summary>
    /// Builds the API + identity scopes required for MsClaw Entra authentication.
    /// </summary>
    public static IReadOnlyList<string> Build(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("ClientId is required to build auth scopes.", nameof(clientId));
        }

        return BaseScopes
            .Concat([$"api://{clientId}/access_as_user"])
            .ToArray();
    }
}

/// <summary>
/// Creates MSAL public client applications and persists their token cache under <c>~/.msclaw/msal-cache.bin</c>.
/// </summary>
public sealed class MsalPublicClientFactory : IMsalPublicClientFactory
{
    private static readonly SemaphoreSlim CacheFileGate = new(1, 1);
    private readonly string cachePath;

    /// <summary>
    /// Initializes the factory using the default cache path under the current user profile.
    /// </summary>
    public MsalPublicClientFactory()
        : this(GetDefaultCachePath())
    {
    }

    /// <summary>
    /// Initializes the factory using an explicit cache file path.
    /// </summary>
    public MsalPublicClientFactory(string cachePath)
    {
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            throw new ArgumentException("Cache path must be provided.", nameof(cachePath));
        }

        this.cachePath = Path.GetFullPath(cachePath);
    }

    /// <inheritdoc />
    public Task<IPublicClientApplication> CreateAsync(string tenantId, string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(clientId));
        }

        var authority = $"https://login.microsoftonline.com/{tenantId}";
        var app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(authority)
            .WithRedirectUri("http://localhost")
            .Build();

        app.UserTokenCache.SetBeforeAccessAsync(BeforeAccessAsync);
        app.UserTokenCache.SetAfterAccessAsync(AfterAccessAsync);

        return Task.FromResult(app);
    }

    private async Task BeforeAccessAsync(TokenCacheNotificationArgs args)
    {
        await CacheFileGate.WaitAsync(args.CancellationToken);
        try
        {
            if (File.Exists(cachePath))
            {
                var cacheBytes = await File.ReadAllBytesAsync(cachePath, args.CancellationToken);
                args.TokenCache.DeserializeMsalV3(cacheBytes, shouldClearExistingCache: true);
            }
        }
        finally
        {
            CacheFileGate.Release();
        }
    }

    private async Task AfterAccessAsync(TokenCacheNotificationArgs args)
    {
        if (args.HasStateChanged is false)
        {
            return;
        }

        await CacheFileGate.WaitAsync(args.CancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(cachePath)
                ?? throw new InvalidOperationException($"Unable to determine token cache directory for '{cachePath}'.");
            Directory.CreateDirectory(directory);
            var cacheBytes = args.TokenCache.SerializeMsalV3();
            await File.WriteAllBytesAsync(cachePath, cacheBytes, args.CancellationToken);
        }
        finally
        {
            CacheFileGate.Release();
        }
    }

    private static string GetDefaultCachePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Unable to resolve user profile directory for MSAL cache.");
        }

        return Path.Combine(home, ".msclaw", "msal-cache.bin");
    }
}
