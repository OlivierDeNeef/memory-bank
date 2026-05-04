using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryBank.Web.Auth;

namespace MemoryBank.Web.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Initiates the OAuth dance: registers the viewer client (idempotent), generates PKCE
        // verifier+challenge, stores them in a short-lived cookie, redirects to /oauth/authorize.
        app.MapGet("/auth/login", (HttpContext ctx, ViewerAuthService auth, string? return_to) =>
        {
            var callback = auth.EnsureRegistered(ctx);
            var state = GenerateOpaque(16);
            var verifier = GenerateOpaque(32);
            var challenge = ComputePkceChallenge(verifier);

            var pending = JsonSerializer.Serialize(new PendingLogin
            {
                State = state,
                CodeVerifier = verifier,
                ReturnTo = SanitizeReturnTo(return_to)
            });
            ctx.Response.Cookies.Append(AuthCookies.LoginState, pending, AuthCookies.Transient(ctx));

            var authBase = auth.AuthBaseUrl(ctx);
            var authorizeUrl = $"{authBase}/oauth/authorize"
                + $"?client_id={Uri.EscapeDataString(ViewerAuthService.ClientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(callback)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&code_challenge={Uri.EscapeDataString(challenge)}"
                + "&code_challenge_method=S256"
                + "&response_type=code";

            return Results.Redirect(authorizeUrl);
        });

        // OAuth provider redirects here with ?code=...&state=... after successful login.
        app.MapGet("/auth/callback", (HttpContext ctx, ViewerAuthService auth, string? code, string? state) =>
        {
            var pendingRaw = ctx.Request.Cookies[AuthCookies.LoginState];
            ctx.Response.Cookies.Delete(AuthCookies.LoginState, AuthCookies.Expired(ctx));

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(pendingRaw))
                return Results.BadRequest("Missing or expired login state. Please try signing in again.");

            PendingLogin? pending;
            try { pending = JsonSerializer.Deserialize<PendingLogin>(pendingRaw); }
            catch (JsonException) { pending = null; }
            if (pending is null || pending.State != state)
                return Results.BadRequest("Login state mismatch. Please try signing in again.");

            var consumed = auth.Store.ConsumeAuthCode(code, pending.CodeVerifier);
            if (consumed is null || consumed.ClientId != ViewerAuthService.ClientId)
                return Results.BadRequest("The authorization code is invalid or expired.");

            var pair = auth.Store.IssueTokens(consumed.ClientId, consumed.Username);

            ctx.Response.Cookies.Append(AuthCookies.Access, pair.AccessToken, AuthCookies.Persistent(ctx, pair.AccessTokenExpiresAt));
            ctx.Response.Cookies.Append(AuthCookies.Refresh, pair.RefreshToken, AuthCookies.Persistent(ctx, pair.RefreshTokenExpiresAt));

            return Results.Redirect(string.IsNullOrEmpty(pending.ReturnTo) ? "/" : pending.ReturnTo);
        });

        app.MapPost("/auth/logout", (HttpContext ctx, ViewerAuthService auth) =>
        {
            var refresh = ctx.Request.Cookies[AuthCookies.Refresh];
            if (!string.IsNullOrEmpty(refresh)) auth.Store.RevokeRefreshToken(refresh);

            ctx.Response.Cookies.Delete(AuthCookies.Access, AuthCookies.Expired(ctx));
            ctx.Response.Cookies.Delete(AuthCookies.Refresh, AuthCookies.Expired(ctx));
            return Results.Ok(new { ok = true });
        });
    }

    private static string GenerateOpaque(int bytes)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static string ComputePkceChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    /// Open-redirect guard: only accept a relative path, never an external URL.
    private static string SanitizeReturnTo(string? returnTo)
    {
        if (string.IsNullOrEmpty(returnTo)) return "/";
        if (!returnTo.StartsWith('/')) return "/";
        if (returnTo.StartsWith("//")) return "/";
        return returnTo;
    }

    private class PendingLogin
    {
        public string State { get; set; } = "";
        public string CodeVerifier { get; set; } = "";
        public string ReturnTo { get; set; } = "/";
    }
}
