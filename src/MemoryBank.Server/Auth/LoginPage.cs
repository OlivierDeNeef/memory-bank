using System.Net;
using System.Text;

namespace MemoryBank.Server.Auth;

public record LoginPageParams(
    string ClientId,
    string ClientName,
    string RedirectUri,
    string State,
    string? CodeChallenge,
    string? CodeChallengeMethod);

public static class LoginPage
{
    public static string Render(LoginPageParams p, string? errorMessage)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>MemoryBank — Sign in</title>
              <style>
                :root { color-scheme: light dark; }
                * { box-sizing: border-box; }
                body {
                  margin: 0; min-height: 100vh; display: grid; place-items: center;
                  font: 15px/1.4 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
                  background: #0d1117; color: #e6edf3;
                }
                main { width: 100%; max-width: 360px; padding: 32px 24px; }
                h1 { margin: 0 0 4px; font-size: 22px; font-weight: 600; }
                .subtitle { margin: 0 0 24px; color: #7d8590; font-size: 13px; }
                form { display: grid; gap: 12px; }
                label { display: grid; gap: 6px; font-size: 13px; color: #c9d1d9; }
                input[type=text], input[name=username], input[type=password] {
                  width: 100%; padding: 9px 12px; border-radius: 6px;
                  border: 1px solid #30363d; background: #0d1117; color: #e6edf3;
                  font: inherit;
                }
                input:focus { outline: none; border-color: #58a6ff; box-shadow: 0 0 0 3px rgba(88,166,255,.25); }
                button {
                  margin-top: 8px; padding: 10px 14px; border-radius: 6px;
                  border: 1px solid #2ea04326; background: #238636; color: #fff;
                  font: 600 14px/1 inherit; cursor: pointer;
                }
                button:hover { background: #2ea043; }
                .error {
                  margin: 0 0 16px; padding: 10px 12px; border-radius: 6px;
                  background: #2d1416; border: 1px solid #5c2127; color: #ff7b72;
                  font-size: 13px;
                }
                .footer { margin-top: 20px; font-size: 12px; color: #6e7681; text-align: center; }
              </style>
            </head>
            <body>
              <main>
                <h1>MemoryBank</h1>
            """);

        sb.Append("    <p class=\"subtitle\">Sign in to authorize ");
        sb.Append(WebUtility.HtmlEncode(string.IsNullOrEmpty(p.ClientName) ? "this client" : p.ClientName));
        sb.Append(".</p>\n");

        if (!string.IsNullOrEmpty(errorMessage))
        {
            sb.Append("    <p class=\"error\">");
            sb.Append(WebUtility.HtmlEncode(errorMessage));
            sb.Append("</p>\n");
        }

        sb.Append("    <form method=\"post\" action=\"/oauth/authorize\">\n");
        AppendHidden(sb, "client_id", p.ClientId);
        AppendHidden(sb, "redirect_uri", p.RedirectUri);
        AppendHidden(sb, "state", p.State);
        AppendHidden(sb, "code_challenge", p.CodeChallenge ?? "");
        AppendHidden(sb, "code_challenge_method", p.CodeChallengeMethod ?? "");

        sb.Append("""
              <label>Username
                <input name="username" autocomplete="username" required autofocus>
              </label>
              <label>Password
                <input name="password" type="password" autocomplete="current-password" required>
              </label>
              <button type="submit">Sign in</button>
            </form>
              </main>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    private static void AppendHidden(StringBuilder sb, string name, string value)
    {
        sb.Append("      <input type=\"hidden\" name=\"");
        sb.Append(WebUtility.HtmlEncode(name));
        sb.Append("\" value=\"");
        sb.Append(WebUtility.HtmlEncode(value));
        sb.Append("\">\n");
    }
}
