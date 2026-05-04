using MemoryBank.Core.Storage;

namespace MemoryBank.Web.Auth;

/// <summary>
/// Bridges the viewer to <see cref="OAuthStore"/>. Holds the viewer's stable OAuth client id and
/// derives the auth-server base URL (separate process) and callback URL (this process).
/// </summary>
public class ViewerAuthService
{
    public const string ClientId = "memorybank-viewer";
    public const string ClientName = "MemoryBank Viewer";
    public const string CallbackPath = "/auth/callback";

    private readonly OAuthStore _store;
    private readonly string? _authBaseUrlOverride;
    private readonly Lock _lock = new();
    private string? _registeredCallback;

    public ViewerAuthService(OAuthStore store)
    {
        _store = store;
        _authBaseUrlOverride = Environment.GetEnvironmentVariable("MEMORYBANK_AUTH_BASE_URL");
    }

    public OAuthStore Store => _store;

    /// <summary>
    /// Returns the absolute callback URL on this viewer process and registers (or refreshes)
    /// the viewer's OAuth client with that URL.
    /// </summary>
    public string EnsureRegistered(HttpContext ctx)
    {
        var callback = $"{ctx.Request.Scheme}://{ctx.Request.Host}{CallbackPath}";
        lock (_lock)
        {
            if (_registeredCallback != callback)
            {
                _store.EnsureClient(ClientId, ClientName, new[] { callback });
                _registeredCallback = callback;
            }
        }
        return callback;
    }

    /// <summary>
    /// Returns the base URL for the OAuth authorization server. Defaults to the same host the
    /// viewer is reached on (production setup with a single host); override in dev with the
    /// <c>MEMORYBANK_AUTH_BASE_URL</c> env var when the MCP server is on a different port.
    /// </summary>
    public string AuthBaseUrl(HttpContext ctx)
        => string.IsNullOrEmpty(_authBaseUrlOverride)
            ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
            : _authBaseUrlOverride.TrimEnd('/');
}
