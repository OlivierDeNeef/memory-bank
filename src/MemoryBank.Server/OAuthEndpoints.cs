using System.Text.Json;
using MemoryBank.Core.Storage;
using MemoryBank.Server.Auth;

namespace MemoryBank.Server;

/// <summary>
/// OAuth 2.1 + PKCE endpoints backed by the persistent <see cref="OAuthStore"/>.
/// Single-user: credentials come from <see cref="EnvCredentialValidator"/>.
/// </summary>
public static class OAuthEndpoints
{
    public static void MapOAuth(this WebApplication app, string mcpRoute)
    {
        var resource = mcpRoute.TrimStart('/');

        app.MapGet($"/.well-known/oauth-protected-resource/{resource}", (HttpContext ctx) =>
        {
            var baseUrl = GetBaseUrl(ctx);
            return Results.Json(new
            {
                resource = $"{baseUrl}/{resource}",
                authorization_servers = new[] { baseUrl },
                bearer_methods_supported = new[] { "header" }
            });
        });

        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx) =>
        {
            var baseUrl = GetBaseUrl(ctx);
            return Results.Json(new
            {
                issuer = baseUrl,
                authorization_endpoint = $"{baseUrl}/oauth/authorize",
                token_endpoint = $"{baseUrl}/oauth/token",
                registration_endpoint = $"{baseUrl}/oauth/register",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                code_challenge_methods_supported = new[] { "S256" }
            });
        });

        app.MapPost("/oauth/register", async (HttpContext ctx, OAuthStore store) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);

            var redirectUris = body.TryGetProperty("redirect_uris", out var uris)
                ? uris.EnumerateArray().Select(u => u.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>();

            var clientName = body.TryGetProperty("client_name", out var name)
                ? name.GetString() ?? "unknown"
                : "unknown";

            var client = store.RegisterClient(clientName, redirectUris);

            return Results.Json(new
            {
                client_id = client.ClientId,
                client_name = client.ClientName,
                redirect_uris = client.RedirectUris,
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
                token_endpoint_auth_method = "none"
            }, statusCode: 201);
        });

        // GET /oauth/authorize — render login form preserving OAuth params as hidden fields
        app.MapGet("/oauth/authorize", (HttpContext ctx, OAuthStore store) =>
        {
            var p = ParseAuthorizeParams(ctx.Request.Query);
            var client = store.GetClient(p.ClientId);
            var clientName = client?.ClientName ?? p.ClientId;

            var html = LoginPage.Render(p with { ClientName = clientName }, errorMessage: null);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        // POST /oauth/authorize — validate creds, issue auth code, redirect to client
        app.MapPost("/oauth/authorize", async (HttpContext ctx, OAuthStore store, ICredentialValidator credentials) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var p = new LoginPageParams(
                ClientId: form["client_id"].ToString(),
                ClientName: form["client_id"].ToString(),
                RedirectUri: form["redirect_uri"].ToString(),
                State: form["state"].ToString(),
                CodeChallenge: NullIfEmpty(form["code_challenge"].ToString()),
                CodeChallengeMethod: NullIfEmpty(form["code_challenge_method"].ToString()));

            var username = form["username"].ToString();
            var password = form["password"].ToString();

            if (!credentials.Validate(username, password))
            {
                var client = store.GetClient(p.ClientId);
                var html = LoginPage.Render(
                    p with { ClientName = client?.ClientName ?? p.ClientId },
                    errorMessage: "Invalid username or password.");
                return Results.Content(html, "text/html; charset=utf-8", statusCode: 401);
            }

            var code = store.IssueAuthCode(p.ClientId, p.RedirectUri, p.CodeChallenge, p.CodeChallengeMethod, username);
            var separator = p.RedirectUri.Contains('?') ? "&" : "?";
            var location = $"{p.RedirectUri}{separator}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(p.State)}";
            return Results.Redirect(location);
        });

        app.MapPost("/oauth/token", async (HttpContext ctx, OAuthStore store) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();

            return grantType switch
            {
                "authorization_code" => HandleAuthCodeGrant(form, store),
                "refresh_token" => HandleRefreshGrant(form, store),
                _ => Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400)
            };
        });
    }

    private static IResult HandleAuthCodeGrant(IFormCollection form, OAuthStore store)
    {
        var code = form["code"].ToString();
        var codeVerifier = NullIfEmpty(form["code_verifier"].ToString());

        var consumed = store.ConsumeAuthCode(code, codeVerifier);
        if (consumed is null)
            return Results.Json(new { error = "invalid_grant" }, statusCode: 400);

        var pair = store.IssueTokens(consumed.ClientId, consumed.Username);
        return TokenPairResponse(pair);
    }

    private static IResult HandleRefreshGrant(IFormCollection form, OAuthStore store)
    {
        var refreshToken = form["refresh_token"].ToString();
        if (string.IsNullOrEmpty(refreshToken))
            return Results.Json(new { error = "invalid_request" }, statusCode: 400);

        var pair = store.RefreshTokens(refreshToken);
        if (pair is null)
            return Results.Json(new { error = "invalid_grant" }, statusCode: 400);

        return TokenPairResponse(pair);
    }

    private static IResult TokenPairResponse(OAuthTokenPair pair) => Results.Json(new
    {
        access_token = pair.AccessToken,
        token_type = "Bearer",
        expires_in = (int)(pair.AccessTokenExpiresAt - DateTime.UtcNow).TotalSeconds,
        refresh_token = pair.RefreshToken
    });

    private static LoginPageParams ParseAuthorizeParams(IQueryCollection q) => new(
        ClientId: q["client_id"].ToString(),
        ClientName: q["client_id"].ToString(),
        RedirectUri: q["redirect_uri"].ToString(),
        State: q["state"].ToString(),
        CodeChallenge: NullIfEmpty(q["code_challenge"].ToString()),
        CodeChallengeMethod: NullIfEmpty(q["code_challenge_method"].ToString()));

    private static string GetBaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
