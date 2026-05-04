namespace MemoryBank.Web.Auth;

public static class AuthCookies
{
    public const string Access = "mb_access";
    public const string Refresh = "mb_refresh";
    public const string LoginState = "mb_login_state";

    public static CookieOptions Persistent(HttpContext ctx, DateTime expires) => new()
    {
        HttpOnly = true,
        Secure = ctx.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        Expires = expires
    };

    public static CookieOptions Transient(HttpContext ctx) => new()
    {
        HttpOnly = true,
        Secure = ctx.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        Expires = DateTime.UtcNow.AddMinutes(10)
    };

    public static CookieOptions Expired(HttpContext ctx) => new()
    {
        HttpOnly = true,
        Secure = ctx.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        Expires = DateTime.UnixEpoch
    };
}
