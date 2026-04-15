using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DeepMind.Server;

/// <summary>
/// Minimal OAuth 2.1 endpoints that auto-approve all requests.
/// This satisfies the MCP Streamable HTTP auth handshake for local/trusted use.
/// </summary>
public static class OAuthEndpoints
{
    private static readonly ConcurrentDictionary<string, ClientRegistration> Clients = new();
    private static readonly ConcurrentDictionary<string, AuthCode> AuthCodes = new();
    private static readonly ConcurrentDictionary<string, string> ActiveTokens = new();

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
                grant_types_supported = new[] { "authorization_code" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                code_challenge_methods_supported = new[] { "S256" }
            });
        });

        app.MapPost("/oauth/register", async (HttpContext ctx) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
            var clientId = Guid.NewGuid().ToString("N");

            var redirectUris = body.TryGetProperty("redirect_uris", out var uris)
                ? uris.EnumerateArray().Select(u => u.GetString()!).ToArray()
                : Array.Empty<string>();

            var clientName = body.TryGetProperty("client_name", out var name)
                ? name.GetString() ?? "unknown"
                : "unknown";

            Clients[clientId] = new ClientRegistration(clientId, clientName, redirectUris);

            return Results.Json(new
            {
                client_id = clientId,
                client_name = clientName,
                redirect_uris = redirectUris,
                grant_types = new[] { "authorization_code" },
                response_types = new[] { "code" },
                token_endpoint_auth_method = "none"
            }, statusCode: 201);
        });

        app.MapGet("/oauth/authorize", (HttpContext ctx) =>
        {
            var query = ctx.Request.Query;
            var redirectUri = query["redirect_uri"].ToString();
            var state = query["state"].ToString();
            var codeChallenge = query["code_challenge"].ToString();
            var codeChallengeMethod = query["code_challenge_method"].ToString();
            var clientId = query["client_id"].ToString();

            var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');

            AuthCodes[code] = new AuthCode(clientId, redirectUri, codeChallenge, codeChallengeMethod, DateTime.UtcNow);

            var separator = redirectUri.Contains('?') ? "&" : "?";
            var location = $"{redirectUri}{separator}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";

            return Results.Redirect(location);
        });

        app.MapPost("/oauth/token", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();
            var code = form["code"].ToString();
            var codeVerifier = form["code_verifier"].ToString();

            if (grantType != "authorization_code" || !AuthCodes.TryRemove(code, out var authCode))
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            if (authCode.Issued < DateTime.UtcNow.AddMinutes(-5))
            {
                return Results.Json(new { error = "invalid_grant", error_description = "code expired" }, statusCode: 400);
            }

            if (!string.IsNullOrEmpty(authCode.CodeChallenge))
            {
                var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)))
                    .Replace("+", "-").Replace("/", "_").TrimEnd('=');

                if (expectedChallenge != authCode.CodeChallenge)
                {
                    return Results.Json(new { error = "invalid_grant", error_description = "PKCE verification failed" }, statusCode: 400);
                }
            }

            var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            ActiveTokens[accessToken] = authCode.ClientId;

            return Results.Json(new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = 86400
            });
        });
    }

    private static string GetBaseUrl(HttpContext ctx)
    {
        var request = ctx.Request;
        return $"{request.Scheme}://{request.Host}";
    }

    private record ClientRegistration(string ClientId, string ClientName, string[] RedirectUris);
    private record AuthCode(string ClientId, string RedirectUri, string CodeChallenge, string CodeChallengeMethod, DateTime Issued);
}
