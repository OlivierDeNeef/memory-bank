using MemoryBank.Core.Storage;

namespace MemoryBank.Web.Auth;

public static class AuthMiddleware
{
    /// <summary>
    /// Gates everything except <c>/auth/*</c>, <c>/health</c>, and static assets behind a valid access cookie.
    /// API requests get a JSON 401; HTML requests get redirected to <c>/auth/login</c>.
    /// </summary>
    public static IApplicationBuilder UseViewerAuth(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            if (IsPublicPath(path))
            {
                await next();
                return;
            }

            var store = ctx.RequestServices.GetRequiredService<OAuthStore>();
            if (await TryAuthenticateAsync(ctx, store))
            {
                await next();
                return;
            }

            await RejectAsync(ctx, path);
        });
        return app;
    }

    private static bool IsPublicPath(string path)
    {
        if (path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static Task<bool> TryAuthenticateAsync(HttpContext ctx, OAuthStore store)
    {
        var access = ctx.Request.Cookies[AuthCookies.Access];
        if (!string.IsNullOrEmpty(access) && store.ValidateAccessToken(access) is not null)
        {
            return Task.FromResult(true);
        }

        var refresh = ctx.Request.Cookies[AuthCookies.Refresh];
        if (string.IsNullOrEmpty(refresh)) return Task.FromResult(false);

        var pair = store.RefreshTokens(refresh);
        if (pair is null)
        {
            // Refresh failed — clear stale cookies so the next request retries cleanly.
            ctx.Response.Cookies.Delete(AuthCookies.Access, AuthCookies.Expired(ctx));
            ctx.Response.Cookies.Delete(AuthCookies.Refresh, AuthCookies.Expired(ctx));
            return Task.FromResult(false);
        }

        ctx.Response.Cookies.Append(AuthCookies.Access, pair.AccessToken, AuthCookies.Persistent(ctx, pair.AccessTokenExpiresAt));
        ctx.Response.Cookies.Append(AuthCookies.Refresh, pair.RefreshToken, AuthCookies.Persistent(ctx, pair.RefreshTokenExpiresAt));
        return Task.FromResult(true);
    }

    private static Task RejectAsync(HttpContext ctx, string path)
    {
        if (PrefersJson(ctx))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return ctx.Response.WriteAsJsonAsync(new { error = "unauthenticated" });
        }
        var returnTo = path + (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "");
        ctx.Response.Redirect($"/auth/login?return_to={Uri.EscapeDataString(returnTo)}");
        return Task.CompletedTask;
    }

    private static bool PrefersJson(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api")) return true;
        var accept = ctx.Request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}
