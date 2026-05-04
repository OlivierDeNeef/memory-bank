using MemoryBank.Core.Storage;

namespace MemoryBank.Server.Auth;

public static class BearerTokenMiddleware
{
    /// <summary>
    /// Gates requests under <paramref name="protectedPath"/> behind a valid OAuth bearer token.
    /// On rejection, returns 401 with a <c>WWW-Authenticate</c> header pointing at the resource
    /// metadata endpoint so MCP clients can discover where to authenticate.
    /// </summary>
    public static IApplicationBuilder UseBearerTokenAuth(this WebApplication app, string protectedPath)
    {
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments(protectedPath),
            branch => branch.Use(async (ctx, next) =>
            {
                var authHeader = ctx.Request.Headers.Authorization.ToString();
                if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    await Reject(ctx, protectedPath);
                    return;
                }

                var token = authHeader["Bearer ".Length..].Trim();
                var store = ctx.RequestServices.GetRequiredService<OAuthStore>();
                if (store.ValidateAccessToken(token) is null)
                {
                    await Reject(ctx, protectedPath);
                    return;
                }

                await next();
            }));
        return app;
    }

    private static Task Reject(HttpContext ctx, string protectedPath)
    {
        var resourceMetadataUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/.well-known/oauth-protected-resource{protectedPath}";
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.Headers.WWWAuthenticate =
            $"Bearer realm=\"MemoryBank\", resource_metadata=\"{resourceMetadataUrl}\"";
        return ctx.Response.WriteAsync("Unauthorized");
    }
}
